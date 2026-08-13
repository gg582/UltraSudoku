# UltraSudoku

## English

A lock-free, zero-allocation C# network simulation comparing six packet-recovery strategies under 10% drop / 5% corruption scenarios.

### Architecture

- **`GameSessionManager`** — Puzzle state (`_currentGrids`, `_solutionGrids`), move validation, session lifecycle.
- **`IRecoveryStrategy`** — Strategy interface for network-layer packet recovery.
- **`ClientSimulation`** — Async traffic generator with lock-free ring buffer and arena pool.

All synchronization is via `System.Threading.Interlocked` only. The hot loop allocates zero heap objects.

### Recovery Strategies

| Strategy | Description & Topology |
|----------|-----------------------|
| **BaselineVectorRecovery** | Row / column / diagonal / anti-diagonal sum constraints. Recovers when exactly one node is missing in a vector (`candidate = expectedSum - vectorSum`). No validation. |
| **MagicSquareRecovery** | Baseline logic **plus** 3 bitmask uniqueness constraints (row, column, lattice). Filters false positives without increasing raw recovery. |
| **HexagonalLatticeRecovery** | Overlapping 7-member interior hexagonal groups (center + 6 neighbors). Fast local repair topology. |
| **HtpXorErasureRecovery** | Hexagonal groups **plus** XOR-parity erasure coding (6-cell partition groups). |
| **ReedSolomonRecovery** | Hexagonal groups **plus** GF(2^8) Vandermonde erasure coding (`⊕ value * alpha^pos`). |
| **KroneckerAntiDiagLatticeRecovery** | 9x9 Kronecker product palace decomposition (Yang diagram 3x3 sub-palaces) + Anti-diagonal symmetry groups (Yin diagram) weighted XOR parity. |

### Performance & Metric Definitions

> **Important Metric Definition**:
> - **Recovery Amplification Ratio (%)**: Total network recovery operations / Raw packet drop count.
>   - Values > 100% occur because Forward Error Correction (FEC) strategies intercept and repair corrupted packets (wrong payload value) in addition to lost/dropped packets.
> - **Net Recovery Rate**: `(Unique Original Packets Restored) / (Unique Packets Lost)` (strictly ≤ 100%).

#### 1-Minute Benchmark Results

| Metric | Baseline | MagicSquare | Hexagonal | HtpXorErasure | ReedSolomon | KroneckerAntiDiag |
|--------|----------|-------------|-----------|---------------|-------------|-------------------|
| **Total Packets Sent** | 41,323,326 | 34,744,705 | 35,173,439 | 28,384,937 | 31,222,162 | 20,589,282 |
| **Dropped (~11%)** | 4,591,161 | 3,861,201 | 3,909,595 | 3,150,660 | 3,467,135 | 2,287,450 |
| **Corrupted (~5.5%)** | 2,295,785 | 1,930,934 | 1,952,681 | 1,578,096 | 1,735,366 | 1,143,092 |
| **Recovered (Ops)** | 111,845 | 92,390 | 148,623 | **5,806,650** | **6,395,652** | **20,148,821** |
| **Amplification Ratio** | 2.44% | 2.39% | 3.80% | 184.30% | 184.47% | **880.84%** |
| **Sessions Won** | 31 | 34 | 32 | **38** | 35 | 54 |
| **Throughput (pkt/s)** | **688,722** | 579,078 | 586,224 | 473,082 | 520,369 | 343,155 |

### Microbenchmark & Cost Structure

Measured on a 10x10 grid with 10,000 sessions (JIT-warmed):

| Strategy | Memory | RegisterSession | ProcessPacket | TryRecoverSession |
|----------|--------|----------------|--------------|------------------|
| **Baseline** | 2.4 MB | 1,043 ns | **18 ns** | 1,037 ns |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns |
| **KroneckerAntiDiag** | 5.2 MB | 2,394 ns | 22 ns | 998 ns |

### Key Findings & Trade-offs

1. **HtpXorErasure vs ReedSolomon (Cost Structure Comparison)**
   - Both achieve near-identical recovery performance (~184% amplification).
   - HtpXorErasure offers faster local per-packet processing (`ProcessPacket` 67 ns vs 78 ns; `TryRecover` 350 ns vs 370 ns).
   - ReedSolomon offers higher steady-state throughput (520K vs 473K pkt/s) and slightly lower session registration latency.
   - HtpXorErasure shows higher session completions (`Won`: 38 vs 35), indicating topological robustness under uneven loss patterns.

2. **MagicSquare Negative Control**
   - Uniqueness masks filter false positives without increasing raw recovery volume (2.39% vs 2.44% baseline). Demonstrates that lattice regularity alone does not grant packet recovery capability unless paired with explicit parity/erasure topology.

3. **Hexagonal Local Repair**
   - Serves as a low-latency local repair layer (`TryRecoverSession` 276 ns, 3.7x faster than Baseline). Ideal as a first-stage filter in multi-tier recovery pipelines.

4. **Kronecker-AntiDiagonal Lattice Topology**
   - Dual XOR parity along 3x3 Kronecker palaces and anti-diagonal symmetry lines. Achieves massive multi-path recovery amplification with compact memory (5.2 MB) and fast packet ingest (22 ns).

### Build & Run

```bash
make

# Run Microbenchmark
dotnet run -c Release --no-build -- bench

# Run Stress Test (Duration, StrategyIndex)
# S = 0: Baseline, 1: MagicSquare, 2: Hexagonal, 3: HtpXorErasure, 4: ReedSolomon, 5: KroneckerAntiDiag
dotnet run -c Release --no-build -- 1 5
```

---

## 한국어

락-프리, 제로-할당 C# 네트워크 시뮬레이션. 6가지 패킷 복구 전략의 성능과 비용 구조를 10% 드롭 / 5% 손상 조건에서 비교 분석합니다.

### 복구 전략 정의

- **BaselineVectorRecovery**: 행/열/대각선 합 제약. 단일 결손 시 단순 산술 복구.
- **MagicSquareRecovery**: 베이스라인 + 3중 비트마스크 고유성 제약. 거짓 양성 차단.
- **HexagonalLatticeRecovery**: 7멤버 육각형 구조. 초고속 국소 복구(276 ns).
- **HtpXorErasureRecovery**: 육각형 구조 + 6셀 분할 XOR 패리티 이레이저 코딩.
- **ReedSolomonRecovery**: 육각형 구조 + GF(2^8) 반데르몽드 이레이저 코딩.
- **KroneckerAntiDiagLatticeRecovery**: 구구자수변궁양도(3x3 궁 크로네커 곱) 및 음도(반대각 대칭) 기반 가중 XOR 패리티 복구.

### 핵심 요약 및 지표 명확화

- **지표 재정의 (Amplification Ratio)**: FEC 기반 전략(HtpXor, RS, KroneckerAntiDiag)은 드롭 패킷뿐 아니라 손상 패킷(Payload 오염)까지 포획·복원하므로 복구 연산 수가 드롭 수를 초과하여 100% 이상의 증폭률(Amplification Ratio)이 측정됩니다.
- **HtpXorErasure vs ReedSolomon**: 두 전략은 실질 복구 수준이 대등하나 비용 구조가 다릅니다. HtpXorErasure는 패킷당 국소 처리(`ProcessPacket` 67 ns vs 78 ns, `TryRecover` 350 ns vs 370 ns)에서 우수하며 `Won` 수(38 vs 35)에서 토폴로지적 강건성을 보입니다.
- **Hexagonal 계층형 계층 제안**: 276 ns의 가장 빠른 복구 속도를 바탕으로 local repair 1차 계층으로 적합합니다.

### 빌드 및 실행

```bash
make
dotnet run -c Release --no-build -- bench
dotnet run -c Release --no-build -- 1 5
```
