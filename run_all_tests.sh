#!/bin/bash
export PATH="$HOME/.dotnet:$PATH"
cd /home/yjlee/UltraSudoku

echo "=== BaselineVectorRecovery (10 min) ==="
dotnet run -c Release --no-build -- 10 0 > test_baseline_10min.log 2>&1

echo "=== MagicSquareRecovery (10 min) ==="
dotnet run -c Release --no-build -- 10 1 > test_magic_10min.log 2>&1

echo "=== HexagonalLatticeRecovery (10 min) ==="
dotnet run -c Release --no-build -- 10 2 > test_hex_10min.log 2>&1

echo "=== HtpXorErasureRecovery (10 min) ==="
dotnet run -c Release --no-build -- 10 3 > test_htpxor_10min.log 2>&1

echo "=== ReedSolomonRecovery (10 min) ==="
dotnet run -c Release --no-build -- 10 4 > test_rs_10min.log 2>&1

echo "=== JiSuZisuMagicRecovery (10 min) ==="
dotnet run -c Release --no-build -- 10 5 > test_jisu_10min.log 2>&1

echo "=== All tests complete ==="
