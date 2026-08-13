# UltraSudoku

## English

A lock-free, zero-allocation C# network simulation comparing six packet-recovery strategies under 10% drop / 5% corruption scenarios.

### Architectural Motivation: Algorithmic Exaptation of Historical Lattices

This project is an engineering attempt to **extract and exploit spatial topology mechanisms** from classical mathematical diagrams (specifically the 9x9 Kronecker Product and Anti-Diagonal Symmetry diagrams) to solve modern network erasure problems.

By replacing expensive Galois Field GF(2^8) arithmetic with hierarchical 3x3 Kronecker palace partitions and anti-diagonal orthogonal symmetry groups, we achieve high-rate erasure recovery using pure bitwise XOR operations within a 5.2 MB cache-friendly footprint.

### Architecture & Components

- **`GameSessionManager`** — Puzzle state (`_currentGrid`, `_solutionGrid`), move validation, session lifecycle.
- **`IRecoveryStrategy`** — Strategy interface for network-layer packet recovery.
- **`ClientSimulation`** — Async traffic generator with lock-free ring buffer and arena pool.

All synchronization uses `System.Threading.Interlocked` only. The hot loop allocates zero heap objects.

### Recovery Strategies

| Strategy | Description & Topology |
|----------|-----------------------|
| **BaselineVectorRecovery** | Row / column / diagonal / anti-diagonal sum constraints. Recovers when exactly one node is missing in a vector (`candidate = expectedSum - vectorSum`). No validation. |
| **MagicSquareRecovery** | Baseline logic **plus** 3 bitmask uniqueness constraints (row, column, lattice). Serves as a negative control proving that magic-square regularity alone does not yield packet recovery. |
| **HexagonalLatticeRecovery** | Overlapping 7-member interior hexagonal groups (center + 6 neighbors). Serves as a fast local repair topology (276 ns). |
| **HtpXorErasureRecovery** | Hexagonal groups **plus** XOR-parity erasure coding (6-cell partition groups). |
| **ReedSolomonRecovery** | Hexagonal groups **plus** GF(2^8) Vandermonde erasure coding (`⊕ value * alpha^pos`). |
| **KroneckerAntiDiagLatticeRecovery** | **Exapted Topology Strategy**: 3x3 Kronecker palace decomposition (Yang diagram) + Anti-diagonal symmetry groups (Yin diagram) weighted XOR parity. |

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

10% 드롭 및 5% 패킷 손상(Corruption) 환경에서 6가지 네트워크 패킷 복구 전략의 성능, 메모리 풋프린트, 연산 부하 트레이드오프를 비교 분석하는 락-프리(Lock-free), 제로-할당(Zero-allocation) C# 네트워크 시뮬레이션입니다.

### 동기 및 기술적 의의: 고전 격자 도상의 알고리즘적 차용 (Algorithmic Exaptation)

본 프로젝트는 민속학적 탐구(Ethnocomputing)에 그치지 않고, **고전 수학 도상(9×9 구구자수변궁 양도 및 음도)의 공간적 대칭성과 계층 구조**를 현대 네트워크 패킷 이레이저 코딩(Erasure Coding) 문제 해결에 직접 차용(Exaptation)하는 엔지니어링 실용성을 목표로 합니다.

비싼 Galois Field GF(2^8) 유한체 연산 대신, **3×3 궁(Palace) 크로네커 곱 파티션**과 **반대각 직교 대칭 축(Anti-diagonal Symmetry Group)**을 활용하여 5.2 MB의 L3 캐시 친화적 메모리 풋프린트와 순수 비트 XOR 연산만으로 고성능 패킷 복구 시스템을 구현했습니다.

### 시스템 아키텍처 및 구성요소

- **`GameSessionManager`**: 퍼즐 상태 관리 (`_currentGrid`, `_solutionGrid`), 이동 검증, 세션 라이프사이클 관리.
- **`IRecoveryStrategy`**: 네트워크 레이어 패킷 복구 전략 인터페이스.
- **`ClientSimulation`**: 락-프리 링 버퍼(Lock-Free Ring Buffer)와 아레나 메모리 풀(Arena Pool) 기반의 비동기 트래픽 생성기.

모든 동기화는 `System.Threading.Interlocked`만을 사용하여 수행되며, 패킷 처리 핫 루프(Hot Loop) 내에서 힙 객체 할당(Heap Allocation)은 0개입니다.

### 복구 전략 (Recovery Strategies)

| 전략 | 토폴로지 및 동작 원리 |
|------|----------------------|
| **BaselineVectorRecovery** | 행 / 열 / 대각선 / 역대각선 합 제약 조건. 벡터 내 단일 노드 결손 시 단순 산술 차이 복구 (`candidate = expectedSum - vectorSum`). 검증 로직 없음. |
| **MagicSquareRecovery** | 베이스라인 + 3중 비트마스크 고유성 제약 조건 (행, 열, 전체 격자). 방진의 고유성만으로는 단순 패킷 복구율이 상승하지 않음을 증명하는 대조군 (Negative Control). |
| **HexagonalLatticeRecovery** | 7개 멤버로 구성된 오버랩 육각형 그룹 (중심 + 6개 이웃). 초고속 국소 복구 레이어 (276 ns). |
| **HtpXorErasureRecovery** | 육각형 그룹 + 6셀 분할 XOR 패리티 이레이저 코딩. |
| **ReedSolomonRecovery** | 육각형 그룹 + GF(2^8) 반데르몽드(Vandermonde) 이레이저 코딩 (`⊕ value * alpha^pos`). |
| **KroneckerAntiDiagLatticeRecovery** | **도상 구조 차용 전략**: 3×3 궁 크로네커 곱 분해 (양도) + 반대각 직교 대칭 그룹 (음도) 가중 XOR 패리티. |

### 주요 지표 정의 (Metric Definitions)

> **지표 명확화**:
> - **복구 증폭 비율 (Amplification Ratio, %)**: `총 네트워크 복구 연산 수 / 원시 패킷 드롭 수`
>   - FEC(순방향 오류 수정) 기반 전략(HtpXor, ReedSolomon, KroneckerAntiDiag)은 손실(Dropped) 패킷뿐만 아니라 잘못된 값이 전달된 손상(Corrupted) 패킷까지 포획하여 복원하므로 증폭 비율이 100%를 초과할 수 있습니다.
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

### 주요 탐구 결과 및 엔지니어링 트레이드오프

1. **격자 토폴로지의 알고리즘적 차용 (`KroneckerAntiDiagLatticeRecovery`)**
   - 3×3 궁 분해와 반대각 대칭 축을 차용하여 Galois Field 연산 표 없이 직교 패리티 그룹을 형성함.
   - Reed-Solomon(34.7 MB) 대비 **5.2 MB의 캐시 친화적 메모리**만 사용하며 L3 캐시 내부에서 모든 처리가 완성됨.
   - 패킷 수신당 처리 오버헤드가 **22 ns**로 Reed-Solomon(78 ns) 대비 약 72% 빠름.

2. **HtpXorErasure vs ReedSolomon 비용 구조 분석**
   - 두 기법 모두 대등한 복구 증폭 수준(~184%)을 달성함.
   - HtpXorErasure는 패킷당 국소 처리 속도가 더 빠르고(`ProcessPacket` 67 ns vs 78 ns), 공간적 손실 패턴에서의 강건성(`Won` 38 vs 35)을 보임.

3. **MagicSquare 대조군 (Negative Control)**
   - 방진 자체의 정합성 규칙만으로는 복구 증대가 일어나지 않음을 증명 (2.39% vs 2.44%). 명시적인 패리티 토폴로지와 결합될 때만 의미를 가짐.

4. **Hexagonal 국소 복구 레이어**
   - 276 ns의 가장 빠른 복구 속도를 바탕으로 계층형 복구 아키텍처의 1차 필터로 적합함.

### 빌드 및 실행 방법

```bash
make

# 마이크로벤치마크 실행
dotnet run -c Release --no-build -- bench

# 부하 테스트 실행 (실행시간, 전략인덱스)
# 인덱스: 0: Baseline, 1: MagicSquare, 2: Hexagonal, 3: HtpXorErasure, 4: ReedSolomon, 5: KroneckerAntiDiag
dotnet run -c Release --no-build -- 1 5
```
