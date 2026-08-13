# UltraSudoku

## English

A lock-free, zero-allocation C# simulation comparing six packet-and-block recovery strategies across network streaming and storage array domains.

### Domain Separation & Categorization Matrix

Rather than forcing all historical diagram topologies into a single network FEC use-case, our empirical findings reveal distinct domain suitability based on generation size, decoding latency, and structural locality:

```text
                                  [ Recovery Strategies ]
                                             │
      ┌──────────────────────────────────────┼──────────────────────────────────────┐
      ▼                                      ▼                                      ▼
[ Streaming / High-RTT FEC ]        [ Storage / RAID / LRC ]              [ Controls & Baselines ]
  - HtpXorErasure (6-cell XOR)        - KroneckerAntiDiag (81-cell 9x9)     - MagicSquare (Negative Control)
  - Hexagonal (276 ns Local Repair)   - LRC / Distributed Parity            - BaselineVector / ReedSolomon (MDS)
```

1. **`HtpXorErasure` & `Hexagonal` $\rightarrow$ Streaming & High-RTT Network FEC**
   - Small generation size (6-cell XOR partition). Rapid accumulation allows low-latency online streaming recovery without long framing delays.

2. **`KroneckerAntiDiag` $\rightarrow$ Storage / Object Store / Local Reconstruction Codes (LRC)**
   - **Generation size 81 (9x9 grid)** creates a decoding delay bottleneck in real-time streaming, but becomes a **hierarchical advantage for storage arrays**.
   - **Asymmetric Asynchronous Cost**: `22 ns Write/Ingest Process` vs `998 ns Rebuild/Repair`. In RAID/LRC, normal writes/reads dominate ($>99\%$), making ultra-cheap normal-path updates (22 ns, 5.2 MB) ideal for storage controllers.
   - **3-Tier Failure Domain Hierarchy**:
     $$\text{3x3 Palace Local Repair} \longrightarrow \text{Anti-Diagonal Cross Group Repair} \longrightarrow \text{Global Parity Recovery}$$
   - **Physical Placement Rule**: Logical $3\times3$ neighborhood $\neq$ Same physical failure domain. Logical cells are interleaved across physical drives/nodes to guarantee drive-loss tolerance.

3. **`MagicSquare` $\rightarrow$ Negative Control**
   - Proves that grid regularity or number patterns alone yield zero recovery advantage ($2.39\%$ vs $2.44\%$ baseline).

4. **`ReedSolomon` $\rightarrow$ Algebraic Global MDS Baseline**
   - Standard Galois Field GF($2^8$) benchmark representing maximum algebraic recovery at higher CPU/memory costs.

---

### Architecture & Components

- **`GameSessionManager`** — State management (`_currentGrid`, `_solutionGrid`), move validation, session lifecycle.
- **`IRecoveryStrategy`** — Strategy interface for network & storage layer recovery.
- **`ClientSimulation`** — Async traffic & block IO generator with lock-free ring buffer and arena pool.

All synchronization uses `System.Threading.Interlocked` only. The hot loop allocates zero heap objects.

---

### Strategy Comparison Matrix

| Strategy | Primary Domain | Generation Size | Normal Write/Process | Rebuild/Repair Latency | Memory Footprint | Role |
|----------|----------------|-----------------|----------------------|------------------------|------------------|------|
| **BaselineVector** | Vector Math | 10 cells | 18 ns | 1,037 ns | 2.4 MB | Simple Vector Baseline |
| **MagicSquare** | Negative Control | 100 cells | 17 ns | 1,039 ns | 7.7 MB | Regularity Control |
| **Hexagonal** | Local Repair Filter | 7 cells | 27 ns | **276 ns** | 30.5 MB | Fast First-Line Repair |
| **HtpXorErasure** | Streaming Network FEC | 6 cells | 67 ns | 350 ns | 34.8 MB | Online High-RTT FEC |
| **ReedSolomon** | Global MDS Code | 6 cells | 78 ns | 370 ns | 34.7 MB | Algebraic Global Baseline |
| **KroneckerAntiDiag** | Storage / RAID / LRC | 81 cells (9x9) | **22 ns** | 998 ns | **5.2 MB** | Hierarchical Array LRC |

---

### Microbenchmark & Storage Metric Verification

Measured on a 10x10 grid with 10,000 sessions (JIT-warmed):

| Strategy | Memory | RegisterSession | ProcessPacket (Write Ingest) | TryRecoverSession (Rebuild) | Domain Suitability |
|----------|--------|----------------|------------------------------|-----------------------------|-------------------|
| **Baseline** | 2.4 MB | 1,043 ns | 18 ns | 1,037 ns | Unvalidated Vector |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns | Unused (Negative Control) |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** | Ultra-Fast Local Filter |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns | Real-Time Streaming FEC |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns | Heavy Global MDS |
| **KroneckerAntiDiag** | **5.2 MB** | 2,394 ns | **22 ns** | 998 ns | **High-Throughput Storage Array** |

---

### Storage Array & Storage-Domain Roadmap

For storage domain validation (`KroneckerAntiDiag`), the critical evaluation metrics shift to:
1. **Average Blocks Read to Repair 1 Missing Block** (LRC Efficiency Metric).
2. **Rebuild Bandwidth & Degraded-Read Latency** under single-drive vs dual-drive failures.
3. **Physical Interleaving Mapping**: Mapping logical $9\times 9$ cells across $N$ physical drives to prevent co-located failure domain loss.

---

### Build & Run

```bash
make

# Microbenchmark
dotnet run -c Release --no-build -- bench

# Stress Test (Duration, StrategyIndex)
dotnet run -c Release --no-build -- 1 5
```

---

## 한국어

10% 드롭 및 5% 손상(Corruption) 환경에서 6가지 패킷 및 블록 복구 전략의 성능, 메모리 풋프린트, 도메인별 적합성을 비교 분석하는 락-프리(Lock-free), 제로-할당(Zero-allocation) C# 시뮬레이션입니다.

### 도메인 분리 및 역할 정의 (Domain Separation Matrix)

모든 고전 도상 구조를 하나의 네트워크 FEC 용도로 억지로 맞추는 대신, Generation Size, 복구 지연시간, 공간적 국소성을 기준으로 도메인을 명확히 분리하였습니다:

1. **`HtpXorErasure` & `Hexagonal` $\rightarrow$ 스트리밍 / High-RTT 네트워크 FEC**
   - 작은 세대 크기 (6셀 분할 XOR 그룹). 빠른 누적으로 실시간 온라인 스트리밍 복구에 적합.
2. **`KroneckerAntiDiag` $\rightarrow$ 스토리지 / 객체 저장소 / 지역 복구 코드 (LRC)**
   - **Generation Size 81 (9×9)**: 실시간 스트리밍에서는 축적 지연이 발생하지만, **스토리지 어레이(RAID/LRC)에서는 계층적 구조의 이점**이 됨.
   - **비대칭 비동기 비용**: `22 ns 정상 쓰기(Ingest)` vs `998 ns 장애 복구(Rebuild)`. 정상 입출력이 99% 이상인 저장장치 특성상 22 ns / 5.2 MB의 캐시 친화적 정상 경로가 극히 유리함.
   - **3단계 장애 도메인 계층**: `3×3 궁 현지 복구` $\rightarrow$ `반대각 교차 그룹 복구` $\rightarrow$ `전역 복구`.
   - **물리적 분산 배치 규칙**: 논리적 3×3 인접성이 동일한 물리 드라이브에 배치되지 않도록 인터리빙하여 드라이브 유실 내성 확보.
3. **`MagicSquare` $\rightarrow$ 음성 대조군 (Negative Control)**
   - 단순 방진 정합성이나 수치 규칙만으로는 복구 이득이 전혀 없음(2.39% vs 2.44%)을 증명.
4. **`ReedSolomon` $\rightarrow$ 대수적 전역 MDS 기준선**
   - 높은 CPU/메모리 비용을 지불하고 최대 대수적 복구 능력을 제공하는 전역 MDS 표준.

### 빌드 및 실행 방법

```bash
make
dotnet run -c Release --no-build -- bench
dotnet run -c Release --no-build -- 1 5
```
