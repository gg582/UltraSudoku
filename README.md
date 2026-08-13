# UltraSudoku

## English

A lock-free, zero-allocation C# network simulation comparing six packet-recovery strategies under 10% drop / 5% corruption scenarios.

### Architectural Motivation: Algorithmic Exaptation of Historical Lattices

This project is not an ethnocomputational survey; it is an engineering attempt to **extract and exploit spatial topology mechanisms** from classical mathematical diagrams (specifically the 9x9 Kronecker Product and Anti-Diagonal Symmetry diagrams) to solve modern network erasure problems.

By replacing expensive Galois Field GF(2^8) arithmetic with hierarchical 3x3 Kronecker palace partitions and anti-diagonal orthogonal symmetry groups, we achieve high-rate erasure recovery using pure bitwise XOR operations within a 5.2 MB cache-friendly footprint.

---

### Architecture & Components

- **`GameSessionManager`** — Puzzle state (`_currentGrid`, `_solutionGrid`), move validation, session lifecycle.
- **`IRecoveryStrategy`** — Strategy interface for network-layer packet recovery.
- **`ClientSimulation`** — Async traffic generator with lock-free ring buffer and arena pool.

All synchronization uses `System.Threading.Interlocked` only. The hot loop allocates zero heap objects.

---

### Recovery Strategies

| Strategy | Description & Topology |
|----------|-----------------------|
| **BaselineVectorRecovery** | Row / column / diagonal / anti-diagonal sum constraints. Recovers when exactly one node is missing in a vector (`candidate = expectedSum - vectorSum`). No validation. |
| **MagicSquareRecovery** | Baseline logic **plus** 3 bitmask uniqueness constraints (row, column, lattice). Serves as a negative control proving that magic-square regularity alone does not yield packet recovery. |
| **HexagonalLatticeRecovery** | Overlapping 7-member interior hexagonal groups (center + 6 neighbors). Serves as a fast local repair topology (276 ns). |
| **HtpXorErasureRecovery** | Hexagonal groups **plus** XOR-parity erasure coding (6-cell partition groups). |
| **ReedSolomonRecovery** | Hexagonal groups **plus** GF(2^8) Vandermonde erasure coding (`⊕ value * alpha^pos`). |
| **KroneckerAntiDiagLatticeRecovery** | **Exapted Topology Strategy**: 3x3 Kronecker palace decomposition (Yang diagram) + Anti-diagonal symmetry groups (Yin diagram) weighted XOR parity. |

---

### Performance & Metric Definitions

> **Metric Definitions**:
> - **Recovery Amplification Ratio (%)**: `Total Network Recovery Operations / Raw Packet Drop Count`
>   - Ratios > 100% occur because Forward Error Correction (FEC) strategies intercept and repair corrupted packets (wrong payload value) in addition to dropped packets.
> - **Net Recovery Rate**: `(Unique Original Packets Restored) / (Unique Packets Lost)` (strictly ≤ 100%).

#### 1-Minute Stress Test Results

| Metric | Baseline | MagicSquare | Hexagonal | HtpXorErasure | ReedSolomon | KroneckerAntiDiag |
|--------|----------|-------------|-----------|---------------|-------------|-------------------|
| **Total Packets Sent** | 41,323,326 | 34,744,705 | 35,173,439 | 28,384,937 | 31,222,162 | 20,589,282 |
| **Dropped (~11%)** | 4,591,161 | 3,861,201 | 3,909,595 | 3,150,660 | 3,467,135 | 2,287,450 |
| **Corrupted (~5.5%)** | 2,295,785 | 1,930,934 | 1,952,681 | 1,578,096 | 1,735,366 | 1,143,092 |
| **Recovered (Ops)** | 111,845 | 92,390 | 148,623 | **5,806,650** | **6,395,652** | **20,148,821** |
| **Amplification Ratio** | 2.44% | 2.39% | 3.80% | 184.30% | 184.47% | **880.84%** |
| **Sessions Won** | 31 | 34 | 32 | **38** | 35 | **54** |
| **Throughput (pkt/s)** | **688,722** | 579,078 | 586,224 | 473,082 | 520,369 | 343,155 |

---

### Microbenchmark & Cost Structure

Measured on a 10x10 grid with 10,000 sessions (JIT-warmed):

| Strategy | Memory | RegisterSession | ProcessPacket | TryRecoverSession |
|----------|--------|----------------|--------------|------------------|
| **Baseline** | 2.4 MB | 1,043 ns | **18 ns** | 1,037 ns |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns |
| **KroneckerAntiDiag** | **5.2 MB** | 2,394 ns | **22 ns** | 998 ns |

---

### Key Findings & Engineering Trade-offs

1. **Algorithmic Exaptation (`KroneckerAntiDiagLatticeRecovery`)**
   - By borrowing the 3x3 Kronecker palace division and anti-diagonal symmetry axes, we obtain orthogonal parity groups without GF(2^8) lookup tables.
   - Requires only **5.2 MB** of memory (fitting fully in L3 cache), compared to 34.7 MB for Reed-Solomon.
   - Ingest latency per packet is **22 ns** (72% faster than Reed-Solomon's 78 ns).

2. **HtpXorErasure vs ReedSolomon**
   - Both achieve equivalent recovery performance (~184% amplification).
   - HtpXorErasure offers faster local operations (`ProcessPacket` 67 ns vs 78 ns; `TryRecover` 350 ns vs 370 ns) and higher session wins (`Won`: 38 vs 35), showing topological robustness under spatial loss patterns.

3. **MagicSquare as a Negative Control**
   - Proves that grid regularity alone does not yield packet recovery (2.39% vs 2.44% baseline). Regularity is useless for error correction unless engineered into explicit parity topologies.

4. **Hexagonal Local Repair**
   - Fast local repair layer (`TryRecoverSession` 276 ns, 3.7x faster than Baseline). Ideal as a first-stage filter in multi-tier recovery architectures.

---

### Build & Run

```bash
make

# Microbenchmark
dotnet run -c Release --no-build -- bench

# Stress Test (Duration, StrategyIndex)
# S = 0: Baseline, 1: MagicSquare, 2: Hexagonal, 3: HtpXorErasure, 4: ReedSolomon, 5: KroneckerAntiDiag
dotnet run -c Release --no-build -- 1 5
```

---

## 한국어

락-프리, 제로-할당 C# 네트워크 시뮬레이션. 고전 마방진 및 궁 파티션 도상의 기하학적 토폴로지 구조를 현대적 이레이저 패킷 복구 메커니즘으로 이식(Algorithmic Exaptation)하고 6가지 전략을 비교 분석합니다.

### 핵심 엔지니어링 의의

- 단순 민속학적 해석(Ethnocomputing)이 아닌, **구구자수변궁 양도(3x3 궁 크로네커 곱 파티션) 및 음도(반대각 직교 대칭)**의 기하 구조를 복구 토폴로지로 직접 차용함.
- GF(2^8) 갈루아 필드 연산 오버헤드 없이, 5.2 MB L3 캐시-친화적 메모리와 22 ns 패킷 인제스트 속도로 고성능 XOR 이레이저 복구를 달성함.

### 빌드 및 실행

```bash
make
dotnet run -c Release --no-build -- bench
dotnet run -c Release --no-build -- 1 5
```
