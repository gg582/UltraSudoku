# UltraSudoku

## English

A lock-free, zero-allocation C# network simulation comparing six packet-recovery strategies under 10% drop / 5% corruption scenarios.

### Architectural Motivation: Algorithmic Exaptation & Trade-off Shift

This project is an engineering attempt to **extract and exploit spatial topology mechanisms** from classical mathematical diagrams (specifically the 9x9 Kronecker Product and Anti-Diagonal Symmetry diagrams) to solve modern network erasure problems.

Our empirical findings demonstrate a structural trade-off shift:
- **Naive Grid Regularity (`MagicSquare`) is ineffective**: Serves as a Negative Control. Raw grid regularity or number patterns yield no recovery advantage (2.39% vs 2.44% baseline).
- **Extracted Structural Invariants + Modern XOR (`KroneckerAntiDiag`)**: Transforming 3x3 Kronecker palace partitioning and anti-diagonal symmetry into data dependency graphs yields a distinct Pareto frontier:
  $$\text{Cheap Ingest (22 ns) + Compact Memory (5.2 MB) } \leftrightarrow \text{ Higher Deferred Repair Latency (998 ns)}$$

### Architecture & Components

- **`GameSessionManager`** — Puzzle state (`_currentGrid`, `_solutionGrid`), move validation, session lifecycle.
- **`IRecoveryStrategy`** — Strategy interface for network-layer packet recovery.
- **`ClientSimulation`** — Async traffic generator with lock-free ring buffer and arena pool.

All synchronization uses `System.Threading.Interlocked` only. The hot loop allocates zero heap objects.

### Recovery Strategies

| Strategy | Description & Topology |
|----------|-----------------------|
| **BaselineVectorRecovery** | Row / column / diagonal / anti-diagonal sum constraints. Recovers when exactly one node is missing in a vector (`candidate = expectedSum - vectorSum`). No validation. |
| **MagicSquareRecovery** | Baseline logic **plus** 3 bitmask uniqueness constraints. Serves as a Negative Control proving that grid regularity alone yields no recovery advantage. |
| **HexagonalLatticeRecovery** | Overlapping 7-member interior hexagonal groups (center + 6 neighbors). Serves as a fast local repair topology (276 ns). |
| **HtpXorErasureRecovery** | Hexagonal groups **plus** XOR-parity erasure coding (6-cell partition groups). |
| **ReedSolomonRecovery** | Hexagonal groups **plus** GF(2^8) Vandermonde erasure coding (`⊕ value * alpha^pos`). |
| **KroneckerAntiDiagLatticeRecovery** | **Exapted Topology Strategy**: 3x3 Kronecker palace decomposition (Yang diagram) + Anti-diagonal symmetry groups (Yin diagram) weighted XOR parity. |

### Performance & Metric Definitions

> **Metric Definitions & Analysis Requirements**:
> - **Recovery Amplification Ratio (%)**: `Total Network Recovery Operations / Raw Packet Drop Count`
>   - Ratios > 100% occur because Forward Error Correction (FEC) strategies intercept and repair corrupted packets in addition to dropped packets, as well as cascading internal multi-path recoveries.
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

### Key Findings & Engineering Pareto Points

1. **Different Pareto Point via Classical Topology (`KroneckerAntiDiag`)**
   - **Cost Shift**: `Cheap Ingest (22 ns) + Compact Memory (5.2 MB) <-> Expensive Deferred Recovery (998 ns)`.
   - Replaces GF(2^8) math with cache-resident XOR topology, lowering per-packet ingest latency from 78 ns (RS) to 22 ns and memory from 34.7 MB to 5.2 MB.

2. **Negative Control Insight (`MagicSquare`)**
   - $\text{Old Diagram} \not\Rightarrow \text{Useful Coding}$.
   - $\text{Extracted Structural Invariants} + \text{Modern XOR} \Rightarrow \text{Useful Pareto Trade-off}$.

3. **Planned Verification Roadmap**
   - **Net Recovery Rate Normalization**: Explicit tracking of unique original restored vs lost packets.
   - **Normalized Win Rate**: `Won / Sessions Attempted`.
   - **Deterministic Seeded Deterministic Workload**: Testing with locked seeds across uniform, burst, rectangular, and adversarial loss patterns.

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

10% 드롭 및 5% 패킷 손상(Corruption) 환경에서 6가지 네트워크 패킷 복구 전략의 성능, 메모리 풋프린트, 연산 부하 트레이드오프를 비교 분석하는 락-프리(Lock-free), 제로-할당(Zero-allocation) C# 네트워크 시뮬레이션입니다.

### 동기 및 기술적 의의: 파레토 포인트 이동 (Pareto Point Shift)

본 프로젝트는 고전 도상의 단순 수치나 마방진 성질을 컴퓨터에 적용하는 민속학적 탐구가 아닙니다.

실험 데이터는 다음을 명확히 증명합니다:
1. **단순 방진 정합성(`MagicSquare`)의 실패**: 방진의 숫자 배열이나 고유성 규칙만 적용한 경우 복구율 향상이 전혀 없음 (2.39% vs 2.44% 대조군).
2. **구조적 불변성 추출 및 현대 XOR 합성(`KroneckerAntiDiag`)**: 3×3 궁 크로네커 곱 파티션과 반대각 대칭 구조를 데이터 의존관계로 재해석했을 때 비로소 RS와 명확히 다른 새로운 파레토 포인트(Pareto Point)가 형성됨:
   $$\text{싸고 빠른 패킷 수신 (22 ns) + 컴팩트 메모리 (5.2 MB)} \leftrightarrow \text{복구 시점 비용 지불 (998 ns)}$$

### 시스템 아키텍처 및 구성요소

- **`GameSessionManager`**: 퍼즐 상태 관리 (`_currentGrid`, `_solutionGrid`), 이동 검증, 세션 라이프사이클 관리.
- **`IRecoveryStrategy`**: 네트워크 레이어 패킷 복구 전략 인터페이스.
- **`ClientSimulation`**: 락-프리 링 버퍼(Lock-Free Ring Buffer)와 아레나 메모리 풀(Arena Pool) 기반의 비동기 트래픽 생성기.

모든 동기화는 `System.Threading.Interlocked`만을 사용하여 수행되며, 패킷 처리 핫 루프(Hot Loop) 내에서 힙 객체 할당(Heap Allocation)은 0개입니다.

### 복구 전략 (Recovery Strategies)

| 전략 | 토폴로지 및 동작 원리 |
|------|----------------------|
| **BaselineVectorRecovery** | 행 / 열 / 대각선 / 역대각선 합 제약 조건. 단일 결손 시 단순 산술 차이 복구. |
| **MagicSquareRecovery** | 베이스라인 + 3중 비트마스크 고유성 제약 조건. 단순 방진 정합성만으로는 복구 이득이 없음을 밝히는 음성 대조군(Negative Control). |
| **HexagonalLatticeRecovery** | 7개 멤버 오버랩 육각형 그룹. 초고속 국소 복구 레이어 (276 ns). |
| **HtpXorErasureRecovery** | 육각형 그룹 + 6셀 분할 XOR 패리티 이레이저 코딩. |
| **ReedSolomonRecovery** | 육각형 그룹 + GF(2^8) 반데르몽드(Vandermonde) 이레이저 코딩. |
| **KroneckerAntiDiagLatticeRecovery** | **도상 구조 차용 전략**: 3×3 궁 크로네커 곱 분해 (양도) + 반대각 직교 대칭 그룹 (음도) 가중 XOR 패리티. |

### 주요 지표 정의 (Metric Definitions)

> **지표 명확화 및 향후 분석 항목**:
> - **복구 증폭 비율 (Amplification Ratio, %)**: `총 네트워크 복구 연산 수 / 원시 패킷 드롭 수` (FEC 및 쇄도적 다중 경로 복구가 포함된 연산 비율).
> - **순수 복구율 (Net Recovery Rate)**: `(복구된 유일 원본 패킷) / (손실된 유일 원본 패킷)` (엄격히 ≤ 100%).

#### 1분 스트레스 테스트 결과

| 지표 | Baseline | MagicSquare | Hexagonal | HtpXorErasure | ReedSolomon | KroneckerAntiDiag |
|------|----------|-------------|-----------|---------------|-------------|-------------------|
| **총 전송 패킷** | 41,323,326 | 34,744,705 | 35,173,439 | 28,384,937 | 31,222,162 | 20,589,282 |
| **드롭 (~11%)** | 4,591,161 | 3,861,201 | 3,909,595 | 3,150,660 | 3,467,135 | 2,287,450 |
| **손상 (~5.5%)** | 2,295,785 | 1,930,934 | 1,952,681 | 1,578,096 | 1,735,366 | 1,143,092 |
| **복구 연산 수** | 111,845 | 92,390 | 148,623 | **5,806,650** | **6,395,652** | **20,148,821** |
| **증폭 비율** | 2.44% | 2.39% | 3.80% | 184.30% | 184.47% | **880.84%** |
| **세션 완주 (Won)** | 31 | 34 | 32 | **38** | 35 | **54** |
| **처리량 (pkt/s)** | **688,722** | 579,078 | 586,224 | 473,082 | 520,369 | 343,155 |

### 마이크로벤치마크 및 연산 비용 구조

10×10 격자, 10,000 세션 조건 (JIT 워밍업 적용):

| 전략 | 메모리 사용량 | RegisterSession | ProcessPacket | TryRecoverSession |
|------|-------------|----------------|--------------|------------------|
| **Baseline** | 2.4 MB | 1,043 ns | **18 ns** | 1,037 ns |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns |
| **KroneckerAntiDiag** | **5.2 MB** | 2,394 ns | **22 ns** | 998 ns |

### 주요 탐구 결과 및 엔지니어링 파레토 포인트 분석

1. **고전 토폴로지 차용을 통한 파레토 포인트 이동 (`KroneckerAntiDiag`)**
   - **비용 구조의 이동**: `저렴한 패킷 인제스트 (22 ns) + 컴팩트 메모리 (5.2 MB) <-> 복구 시점의 연산 비용 지불 (998 ns)`
   - RS 대비 메모리는 34.7 MB에서 5.2 MB로 절감하고 수신 속도는 78 ns에서 22 ns로 단축시켰으나, 지연된 복구 연산 시 비용을 더 지불함.

2. **Negative Control의 교훈 (`MagicSquare`)**
   - $\text{고전 도상} \not\Rightarrow \text{유용한 코딩}$.
   - $\text{구조적 불변성 추출} + \text{현대적 XOR 패리티} \Rightarrow \text{유의미한 파레토 트레이드오프}$.

3. **향후 검증 고도화 계획**
   - **Net Recovery Rate 세분화**: 손실 원본 대비 유일 복원 비율 명시적 추적.
   - **시도 대비 세션 완주율 정규화**: `Won / Sessions Attempted`.
   - **고정 시드(Fixed Seed) 장애 시나리오**: Uniform, Burst, Rectangular, Adversarial loss 시나리오별 통제 비교.

### 빌드 및 실행 방법

```bash
make

# 마이크로벤치마크 실행
dotnet run -c Release --no-build -- bench

# 부하 테스트 실행 (실행시간, 전략인덱스)
dotnet run -c Release --no-build -- 1 5
```
