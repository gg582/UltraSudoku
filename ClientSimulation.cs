using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace UltraSudoku
{
    public sealed class Program
    {
        private const int PacketChunkSize = 64;
        private const int PacketChunkCount = 65536;
        private const int PacketQueueCapacity = 65536;
        private const int MaxActiveSessionsTarget = 10000;
        private const int MaxGridCells = 100;

        private static readonly GridArenaPool PacketPool = new GridArenaPool(PacketChunkSize, PacketChunkCount);
        private static readonly LockFreeRingBuffer PacketQueue = new LockFreeRingBuffer(PacketQueueCapacity);
        private static readonly GameSessionManager GameManager = new GameSessionManager();
        private static IRecoveryStrategy RecoveryStrategy = new BaselineVectorRecovery();

        private static long _totalSent;
        private static long _totalDropped;
        private static long _totalCorrupted;
        private static long _totalRecovered;
        private static long _totalSessions;
        private static readonly CancellationTokenSource Cts = new CancellationTokenSource();

        public static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "bench")
            {
                Benchmark.Run();
                return;
            }

            int durationSeconds = 0;
            if (args.Length > 0 && int.TryParse(args[0], out int minutes) && minutes > 0)
            {
                durationSeconds = minutes * 60;
            }

            if (args.Length > 1)
            {
                if (args[1] == "1") RecoveryStrategy = new MagicSquareRecovery();
                else if (args[1] == "2") RecoveryStrategy = new HexagonalLatticeRecovery();
                else if (args[1] == "3") RecoveryStrategy = new HtpXorErasureRecovery();
                else if (args[1] == "4") RecoveryStrategy = new ReedSolomonRecovery();
                else if (args[1] == "5") RecoveryStrategy = new KroneckerAntiDiagLatticeRecovery();
            }

            Console.WriteLine("\u001b[1;37mUltra Sudoku Lattice Server Boot Sequence\u001b[0m");
            Task serverTask = Task.Run(ServerListenerLoop);
            int clientCount = Environment.ProcessorCount;
            Task[] clientTasks = new Task[clientCount];
            for (int i = 0; i < clientCount; i++)
            {
                clientTasks[i] = Task.Run(ClientLoadGenerator);
            }
            Task renderTask = Task.Run(RenderingLoop);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                Cts.Cancel();
            };

            if (durationSeconds > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(durationSeconds * 1000);
                    Cts.Cancel();
                });
            }

            await Task.WhenAll(clientTasks);
            Cts.Cancel();
            await serverTask;
            await renderTask;
            Cts.Dispose();

            if (durationSeconds > 0)
            {
                PrintFinalSummary(durationSeconds);
            }
        }

        private static void PrintFinalSummary(int durationSeconds)
        {
            long sent = Interlocked.Read(ref _totalSent);
            long dropped = Interlocked.Read(ref _totalDropped);
            long corrupted = Interlocked.Read(ref _totalCorrupted);
            long recovered = Interlocked.Read(ref _totalRecovered);
            long sessions = Interlocked.Read(ref _totalSessions);
            int active = GameManager.CountActiveSessions();
            int won = GameManager.CountWonSessions();
            double dropRate = sent > 0 ? (double)dropped / sent * 100.0 : 0.0;
            double corruptRate = sent > 0 ? (double)corrupted / sent * 100.0 : 0.0;
            double recoverRate = dropped > 0 ? (double)recovered / dropped * 100.0 : 0.0;
            double pps = (double)sent / durationSeconds;

            Console.WriteLine("\n\u001b[1;36m══════════════════════════════════════════════════════════════\u001b[0m");
            Console.WriteLine("\u001b[1;37m                    FINAL STRESS TEST SUMMARY                 \u001b[0m");
            Console.WriteLine("\u001b[1;36m══════════════════════════════════════════════════════════════\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Duration:              {durationSeconds / 60} minutes\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Total Packets Sent:    {sent,20:N0}\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Dropped:               {dropped,20:N0} ({dropRate:F2}%)\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Corrupted:             {corrupted,20:N0} ({corruptRate:F2}%)\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Recovered (Network):   {recovered,20:N0} ({recoverRate:F2}% of dropped)\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Sessions Created:      {sessions,20:N0}\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Sessions Active:       {active,20:N0}\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Sessions Won:          {won,20:N0}\u001b[0m");
            Console.WriteLine($"\u001b[1;37m  Average Packets/sec:   {pps,20:N0}\u001b[0m");
            Console.WriteLine("\u001b[1;36m══════════════════════════════════════════════════════════════\u001b[0m");
        }

        private static void ServerListenerLoop()
        {
            var recoveredBuffer = new MovePacket[MaxGridCells];
            long loopCount = 0;
            while (!Cts.IsCancellationRequested)
            {
                if (loopCount % 100 == 0)
                {
                    int active = GameManager.CountActiveSessions();
                    if (active < MaxActiveSessionsTarget)
                    {
                        if (GameManager.TryCreateSession(out int slot, out uint sid))
                        {
                            RecoveryStrategy.RegisterSession(slot, sid, GameManager._gridSizes[slot], GameManager._expectedSums[slot], GameManager.GetCurrentSpan(slot), GameManager.GetSolutionSpan(slot));
                            Interlocked.Increment(ref _totalSessions);
                        }
                    }
                }
                if (loopCount % 10000 == 0)
                {
                    for (int i = 0; i < GameSessionManager.MaxSessions; i++)
                    {
                        GameManager.ResetCompletedSession(i);
                    }
                }
                loopCount++;

                if (PacketQueue.TryDequeue(out int chunkIndex))
                {
                    var span = PacketPool.GetChunkSpan(chunkIndex);
                    MovePacket packet = PacketSerializer.ReadPacket(span);
                    PacketPool.ReleaseChunk(chunkIndex);

                    int slotIndex = (int)(packet.SessionId & 0xFFFF);
                    int gen = (int)(packet.SessionId >> 16);
                    if (slotIndex < 0 || slotIndex >= GameSessionManager.MaxSessions)
                    {
                        continue;
                    }
                    if (GameManager._sessionStates[slotIndex] != 1)
                    {
                        continue;
                    }
                    if (GameManager._slotGenerations[slotIndex] != gen)
                    {
                        continue;
                    }

                    RecoveryStrategy.ProcessPacket(slotIndex, packet);
                    int recCount = RecoveryStrategy.TryRecoverSession(slotIndex, recoveredBuffer);
                    for (int i = 0; i < recCount; i++)
                    {
                        GameManager.ApplyMove(slotIndex, recoveredBuffer[i]);
                        Interlocked.Increment(ref _totalRecovered);
                    }
                    GameManager.ApplyMove(slotIndex, packet);
                }
                else
                {
                    Thread.SpinWait(1);
                }
            }
        }

        private static async Task ClientLoadGenerator()
        {
            Random random = new Random(Environment.CurrentManagedThreadId ^ (int)Stopwatch.GetTimestamp());
            int spinCount = 0;
            while (!Cts.IsCancellationRequested)
            {
                if (!GameManager.TryGetRandomActiveSession(random, out int slotIndex, out uint sessionId, out int gridSize))
                {
                    await Task.Delay(1);
                    continue;
                }

                if (!GameManager.TryGetRandomBlankCell(random, slotIndex, out int row, out int col, out byte value))
                {
                    continue;
                }

                double fate = random.NextDouble();
                if (fate < 0.10)
                {
                    Interlocked.Increment(ref _totalDropped);
                    continue;
                }
                if (fate < 0.15)
                {
                    value = (byte)(random.Next(1, 101));
                    Interlocked.Increment(ref _totalCorrupted);
                }

                MovePacket packet = new MovePacket
                {
                    SessionId = sessionId,
                    Row = (byte)row,
                    Col = (byte)col,
                    Value = value,
                    Sequence = 0
                };

                int chunk = PacketPool.LeaseChunk();
                if (chunk == -1)
                {
                    if (++spinCount > 100)
                    {
                        await Task.Yield();
                        spinCount = 0;
                    }
                    else
                    {
                        Thread.SpinWait(1);
                    }
                    continue;
                }
                spinCount = 0;

                WritePacketToChunk(chunk, packet);
                Interlocked.Increment(ref _totalSent);

                while (!PacketQueue.TryEnqueue(chunk))
                {
                    if (++spinCount > 100)
                    {
                        await Task.Yield();
                        spinCount = 0;
                    }
                    else
                    {
                        Thread.SpinWait(1);
                    }
                }
                spinCount = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WritePacketToChunk(int chunkIndex, MovePacket packet)
        {
            var span = PacketPool.GetChunkSpan(chunkIndex);
            PacketSerializer.WritePacket(span, packet);
        }

        private static async Task RenderingLoop()
        {
            while (!Cts.IsCancellationRequested)
            {
                await Task.Delay(200);
                RenderFrame();
            }
        }

        private static void RenderFrame()
        {
            long sent = Interlocked.Read(ref _totalSent);
            long dropped = Interlocked.Read(ref _totalDropped);
            long corrupted = Interlocked.Read(ref _totalCorrupted);
            long recovered = Interlocked.Read(ref _totalRecovered);
            long sessions = Interlocked.Read(ref _totalSessions);
            int active = GameManager.CountActiveSessions();
            int won = GameManager.CountWonSessions();
            Console.Write("\u001b[H\u001b[2J");
            Console.WriteLine("\u001b[1;36m╔══════════════════════════════════════════════════════════════╗\u001b[0m");
            Console.WriteLine("\u001b[1;36m║     ORTHOGONAL LATIN SQUARE LATTICE VERIFICATION SYSTEM      ║\u001b[0m");
            Console.WriteLine("\u001b[1;36m╠══════════════════════════════════════════════════════════════╣\u001b[0m");
            Console.WriteLine($"\u001b[1;37m║  TOTAL PACKETS TRANSMITTED:    {sent,12}                    ║\u001b[0m");
            Console.WriteLine($"\u001b[1;37m║  DROPPED PACKETS:              {dropped,12}                    ║\u001b[0m");
            Console.WriteLine($"\u001b[1;37m║  CORRUPTED PACKETS:            {corrupted,12}                    ║\u001b[0m");
            Console.WriteLine($"\u001b[1;37m║  RECOVERED (NETWORK LAYER):    \u001b[1;33m{recovered,12}\u001b[1;37m                    ║\u001b[0m");
            Console.WriteLine("\u001b[1;36m╠══════════════════════════════════════════════════════════════╣\u001b[0m");
            Console.WriteLine($"\u001b[1;37m║  SESSIONS:     {sessions,12}  ACTIVE: {active,12}  WON: \u001b[1;32m{won,12}\u001b[1;37m  ║\u001b[0m");
            Console.WriteLine("\u001b[1;36m╚══════════════════════════════════════════════════════════════╝\u001b[0m");
            if (dropped > 0 && recovered > 0)
            {
                Console.WriteLine("\u001b[1;33m  STATUS: INFRASTRUCTURE PACKET RECOVERY ACTIVE               \u001b[0m");
            }
            else if (dropped == 0)
            {
                Console.WriteLine("\u001b[1;32m  STATUS: HAPPY PATH - ALL NETWORK VECTORS NOMINAL            \u001b[0m");
            }
            else
            {
                Console.WriteLine("\u001b[1;31m  STATUS: UNRECOVERABLE VECTOR OUTAGE DETECTED                \u001b[0m");
            }
        }
    }
}
