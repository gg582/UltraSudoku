using System;
using System.Diagnostics;

namespace UltraSudoku
{
    public static class TestMultiLoss
    {
        public static void Run()
        {
            const int GridSize = 10;
            const int Cells = GridSize * GridSize;
            const int SlotPool = 128;
            const int TotalSessions = 2000;

            byte[] solution = new byte[Cells];
            OrthogonalLatinSquareGenerator.Generate(GridSize, solution);
            uint expectedSum = OrthogonalLatinSquareGenerator.ComputeMagicSum(GridSize);

            byte[] current = new byte[Cells];
            int blankCount = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (i % 2 == 0) { current[i] = 0; blankCount++; }
                else { current[i] = solution[i]; }
            }

            var packets = new MovePacket[blankCount];
            int pi = 0;
            for (int i = 0; i < Cells; i++)
            {
                if (current[i] == 0)
                {
                    packets[pi++] = new MovePacket
                    {
                        Row = (byte)(i / GridSize),
                        Col = (byte)(i % GridSize),
                        Value = solution[i],
                    };
                }
            }

            var recoverBuf = new MovePacket[Cells];

            Console.WriteLine("=== Loss & Recovery Success Rate Simulation ===");
            for (int lostPerSession = 1; lostPerSession <= 4; lostPerSession++)
            {
                Console.WriteLine($"\n--- Missing Chunks Per Session: {lostPerSession} ---");
                Console.WriteLine($"{"Strategy",-16} {"Total Lost",12} {"Recovered",12} {"Success Rate",14}");
                Console.WriteLine(new string('-', 56));

                var specs = new (string Name, Func<IRecoveryStrategy> Factory)[]
                {
                    ("Baseline      ", () => new BaselineVectorRecovery()),
                    ("TASFA         ", () => new TasfaRecovery()),
                    ("ReedSolomon   ", () => new ReedSolomonRecovery()),
                };

                foreach (var spec in specs)
                {
                    IRecoveryStrategy strat = spec.Factory();
                    int totalLost = 0;
                    int totalRecovered = 0;

                    Random rnd = new Random(42);
                    for (int iter = 0; iter < TotalSessions; iter++)
                    {
                        int slot = iter % SlotPool;
                        strat.RegisterSession(slot, (uint)iter, GridSize, expectedSum, current, solution);

                        // Randomly drop `lostPerSession` distinct packets
                        var droppedIndices = new System.Collections.Generic.HashSet<int>();
                        while (droppedIndices.Count < lostPerSession)
                        {
                            droppedIndices.Add(rnd.Next(packets.Length));
                        }

                        for (int pk = 0; pk < packets.Length; pk++)
                        {
                            if (droppedIndices.Contains(pk)) continue;
                            var p = packets[pk];
                            p.SessionId = (uint)(iter & 0xFFFF);
                            strat.ProcessPacket(slot, p);
                        }

                        totalLost += lostPerSession;
                        int rec = strat.TryRecoverSession(slot, recoverBuf);
                        totalRecovered += rec;
                    }

                    double rate = (double)totalRecovered / totalLost * 100.0;
                    Console.WriteLine($"{spec.Name,-16} {totalLost,12:N0} {totalRecovered,12:N0} {rate,13:F1}%");
                }
            }
        }
    }
}
