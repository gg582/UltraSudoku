using System;
using System.Diagnostics;

namespace UltraSudoku
{
    public static class Benchmark
    {
        private const int GridSize = 10;
        private const int Cells = GridSize * GridSize;
        private const int WarmupSessions = 1000;
        private const int MeasureSessions = 10_000;
        // Cycle through this many slots to avoid aliasing
        private const int SlotPool = 128;

        public static void Run()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== UltraSudoku Strategy Microbenchmark ===");
            Console.WriteLine($"Grid: {GridSize}x{GridSize}  " +
                              $"Warmup: {WarmupSessions} sessions  " +
                              $"Measure: {MeasureSessions} sessions  " +
                              $"SlotPool: {SlotPool}");
            Console.WriteLine();

            // Build solution grid
            byte[] solution = new byte[Cells];
            OrthogonalLatinSquareGenerator.Generate(GridSize, solution);
            uint expectedSum = OrthogonalLatinSquareGenerator.ComputeMagicSum(GridSize);

            // current grid: blank every other cell (50%)
            byte[] current = new byte[Cells];
            int blankCount = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (i % 2 == 0) { current[i] = 0; blankCount++; }
                else             { current[i] = solution[i]; }
            }

            // Pre-build packet list for all blank cells
            var packets = new MovePacket[blankCount];
            int pi = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (current[i] == 0)
                {
                    packets[pi++] = new MovePacket
                    {
                        Row   = (byte)(i / GridSize),
                        Col   = (byte)(i % GridSize),
                        Value = solution[i],
                    };
                }
            }

            var recoverBuf = new MovePacket[Cells];

            // ── Strategy factory list ────────────────────────────────────────
            var specs = new (string Name, Func<IRecoveryStrategy> Factory)[]
            {
                ("Baseline      ", () => new BaselineVectorRecovery()),
                ("MagicSquare   ", () => new MagicSquareRecovery()),
                ("Hexagonal     ", () => new HexagonalLatticeRecovery()),
                ("HtpXorErasure ", () => new HtpXorErasureRecovery()),
                ("ReedSolomon   ", () => new ReedSolomonRecovery()),
                ("KroneckerLatt ", () => new KroneckerAntiDiagLatticeRecovery()),
            };

            Console.WriteLine($"{"Strategy",-16} {"Memory":>10} {"Register":>12} {"ProcessPkt":>12} {"TryRecover":>12}");
            Console.WriteLine(new string('-', 68));

            foreach (var spec in specs)
            {
                // ── Memory ────────────────────────────────────────────────────
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                long memBefore = GC.GetTotalMemory(true);
                IRecoveryStrategy strat = spec.Factory();
                long memAfter = GC.GetTotalMemory(true);
                double memMB = (memAfter - memBefore) / 1_048_576.0;

                // ── Warmup ────────────────────────────────────────────────────
                for (int w = 0; w < WarmupSessions; w++)
                {
                    int slot = w % SlotPool;
                    strat.RegisterSession(slot, (uint)w, GridSize, expectedSum, current, solution);
                    foreach (var pkt in packets)
                    {
                        var p = pkt; p.SessionId = (uint)(w & 0xFFFF);
                        strat.ProcessPacket(slot, p);
                    }
                    strat.TryRecoverSession(slot, recoverBuf);
                }

                // ── Benchmark 1: RegisterSession ──────────────────────────────
                var sw = Stopwatch.StartNew();
                for (int iter = 0; iter < MeasureSessions; iter++)
                {
                    strat.RegisterSession(iter % SlotPool, (uint)iter, GridSize, expectedSum, current, solution);
                }
                sw.Stop();
                double registerNs = sw.Elapsed.TotalNanoseconds / MeasureSessions;

                // ── Benchmark 2: ProcessPacket ────────────────────────────────
                // "register + all packets" loop; subtract register cost below
                long totalPkts = 0;
                sw = Stopwatch.StartNew();
                for (int iter = 0; iter < MeasureSessions; iter++)
                {
                    int slot = iter % SlotPool;
                    strat.RegisterSession(slot, (uint)iter, GridSize, expectedSum, current, solution);
                    foreach (var pkt in packets)
                    {
                        var p = pkt; p.SessionId = (uint)(iter & 0xFFFF);
                        strat.ProcessPacket(slot, p);
                        totalPkts++;
                    }
                }
                sw.Stop();
                double wholeNs = sw.Elapsed.TotalNanoseconds;
                // ns per ProcessPacket = (total - register overhead) / packet count
                double processNs = (wholeNs - registerNs * MeasureSessions) / totalPkts;

                // ── Benchmark 3: TryRecoverSession ────────────────────────────
                // Prepare sessions: all packets except the last blank cell sent
                for (int iter = 0; iter < MeasureSessions; iter++)
                {
                    int slot = iter % SlotPool;
                    strat.RegisterSession(slot, (uint)iter, GridSize, expectedSum, current, solution);
                    for (int pk = 0; pk < packets.Length - 1; pk++)
                    {
                        var p = packets[pk]; p.SessionId = (uint)(iter & 0xFFFF);
                        strat.ProcessPacket(slot, p);
                    }
                }
                sw = Stopwatch.StartNew();
                for (int iter = 0; iter < MeasureSessions; iter++)
                {
                    strat.TryRecoverSession(iter % SlotPool, recoverBuf);
                }
                sw.Stop();
                double recoverNs = sw.Elapsed.TotalNanoseconds / MeasureSessions;

                Console.WriteLine(
                    $"{spec.Name,-16} {memMB,8:F1} MB " +
                    $"{registerNs,9:F0} ns " +
                    $"{processNs,9:F0} ns " +
                    $"{recoverNs,9:F0} ns");
            }

            Console.WriteLine();
            Console.WriteLine("Columns:");
            Console.WriteLine("  Memory         — heap allocated at construction (GC delta, MB)");
            Console.WriteLine("  Register       — RegisterSession ns/call");
            Console.WriteLine("  ProcessPkt     — ProcessPacket ns/call (full hot path)");
            Console.WriteLine("  TryRecover     — TryRecoverSession ns/call (1 missing cell)");
        }
    }
}
