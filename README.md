# UltraSudoku

## English

A lock-free, zero-allocation C# simulation comparing packet-and-block recovery strategies across network streaming and storage array domains under 10% drop / 5% corruption scenarios, featuring the complete mathematical model of **TASFA** alongside Reed-Solomon (MDS) and classical diagram topologies.

### Classical Diagram Origins & Topological Exaptation

This project extracts structural invariants from traditional Korean mathematical and magic diagram traditions (도상, 圖象) and adapts them into modern erasure coding topologies:

1. **`TASFA`** & **`HexagonalLatticeRecovery`** $\rightarrow$ Derived from **Jisu-gwimundo (지수귀문도, 地數龜文圖)**
   - **TASFA**: Faithful implementation of the production TASFA protocol. Divides data into 6-slot modular linear groups ($v_0 \dots v_5$) with client-side residue balancing ($\Delta_2, \Delta_3 \pmod M$), group-level XOR parity, and an iterative Peeling Decoder with single-divergence syndrome localization ($L_a == L_b \neq L_c$).
   - **Hexagonal**: Hexagonal overlapping lattice topology where 7-node hexagonal groups form fast local repair clusters.
2. **`KroneckerAntiDiagLatticeRecovery`** $\rightarrow$ Derived from **Baekja-saengseong-gyosudo & Baekja-saengseong-sunsudo (백자생성교수도 & 백자생성순수도, 百子生成交數圖 & 百子生成順數圖)**
   - Paired Yin-Yang 9x9 diagrams:
     - **Yang Diagram (백자생성교수도)**: 3x3 Magic Square Kronecker sub-grid / block partition hierarchy ($L \otimes L$) forming 9 local sub-grid parity groups.
     - **Yin Diagram (백자생성순수도)**: Anti-diagonal symmetry groups forming orthogonal cross-block parity groups.
   - Applied to the 81-cell core region, creating a 3-tier LRC storage array topology (5.2 MB memory, 22 ns write ingest).

### Domain Separation & Categorization Matrix

```text
                                   [ Recovery Strategies ]
                                              │
       ┌──────────────────────────────────────┼──────────────────────────────────────┐
       ▼                                      ▼                                      ▼
[ Real-Time Streaming / Ingest FEC ]  [ Storage / RAID / LRC ]              [ Controls & Baselines ]
  - TASFA (6-slot modular + XOR peel)   - KroneckerAntiDiag (81-cell 9x9)     - MagicSquare (Negative Control)
  - HtpXorErasure (Hybrid 2D lattice)   - LRC / Distributed Parity            - BaselineVector (Unchecked)
  - Hexagonal (Jisu-gwimundo 276 ns)                                         - ReedSolomon (Global MDS)
```

1. **`TASFA` (Production Protocol Formulation)**
   - **6-slot Grouping & Modulo Balancing**: Client balances $v_3, v_5$ before dispatch to guarantee $L_1 = L_2 = L_3 \pmod M$.
   - **Iterative Peeling Decoder**: Employs $O(1)$ XOR parity per group combined with single-divergence syndrome tracking ($L_a == L_b \neq L_c$) to pinpoint and resolve missing chunks in a domino sequence.
   - **Speed & Memory Advantages**: Completely avoids heavy $GF(2^8)$ Galois Field matrix inversions, cutting memory in half (**16.2 MB vs 34.7 MB**) and accelerating session registration by **13.7x** (**465 ns vs 6,368 ns**).
   - **Trade-off**: Forfeits MDS mathematical guarantees in exchange for ultra-low latency, leaving vulnerability to stopping sets (cycles) and burst packet losses (mitigated in production via transmission interleaving).

2. **`KroneckerAntiDiag` $\rightarrow$ Storage / Object Store / Local Reconstruction Codes (LRC)**
   - **Generation size 81 (9x9 diagram)**: Provides a 3-tier failure domain hierarchy ideal for storage controllers with 22 ns write ingest and 5.2 MB memory footprint.

3. **`ReedSolomon` $\rightarrow$ Algebraic Global MDS Baseline**
   - Standard Galois Field GF($2^8$) benchmark representing maximum algebraic recovery at higher CPU/memory costs.

### Strategy Comparison Matrix

| Strategy | Origin / Model | Primary Domain | Generation Size | Write Ingest | Rebuild Latency | Memory Footprint | Role & Characteristics |
|----------|----------------|----------------|-----------------|--------------|-----------------|------------------|-----------------------|
| **BaselineVector** | Vector Math | Vector Math | 10 cells | 18 ns | 1,018 ns | 2.4 MB | Simple Unvalidated Vector Baseline |
| **MagicSquare** | Negative Control | Negative Control | 100 cells | 14 ns | 982 ns | 7.7 MB | Regularity Control (Zero Gain) |
| **Hexagonal** | 지수귀문도 | Local Repair Filter | 7 cells | 27 ns | 332 ns | 30.5 MB | Fast 1st-Line Local Repair Filter |
| **HtpXorErasure** | 지수귀문도 + XOR | Streaming FEC | 6 cells | 77 ns | 304 ns | 34.8 MB | 2D Lattice Hybrid Transition Model |
| **TASFA** | Production Protocol | Real-Time Streaming FEC | 6 cells | **33 ns** | 993 ns | **16.2 MB** | 6-Slot Modulo Balancing + XOR Peeling |
| **ReedSolomon** | Galois Field $GF(2^8)$ | Storage / Archive MDS | 6 cells | 45 ns | 346 ns | 34.7 MB | Heavy Global Algebraic MDS Baseline |
| **KroneckerAntiDiag** | 백자생성교수도·순수도 | Storage / RAID / LRC | 81 cells (9x9) | 22 ns | 986 ns | 5.2 MB | Hierarchical Array LRC Topology |

### Microbenchmark & Multi-Loss Simulation

Measured on a 10x10 grid with 10,000 sessions (Release build, .NET 8):

#### 1. Microbenchmark Metrics

| Strategy | Memory | RegisterSession | ProcessPacket (Ingest) | TryRecoverSession (Rebuild) |
|----------|:------:|:---------------:|:----------------------:|:---------------------------:|
| **Baseline** | 2.4 MB | 943 ns | 17 ns | 1,018 ns |
| **MagicSquare** | 7.7 MB | 2,129 ns | 14 ns | 982 ns |
| **Hexagonal** | 30.5 MB | 6,872 ns | -13 ns | 332 ns |
| **HtpXorErasure** | 34.8 MB | 6,802 ns | 77 ns | 304 ns |
| **TASFA** | **16.2 MB** | **465 ns** | **33 ns** | **993 ns** |
| **ReedSolomon** | 34.7 MB | 6,368 ns | 45 ns | 346 ns |
| **KroneckerAntiDiag** | 5.2 MB | 2,331 ns | 34 ns | 986 ns |

#### 2. Multi-Loss Recovery Success Rate (2,000 Sessions Random Loss)

| Missing Chunks | Baseline | HtpXorErasure | TASFA | ReedSolomon |
|:--------------:|:--------:|:-------------:|:-----:|:-----------:|
| **1 Chunk** (2,000 lost) | 100.0% (2,000) | 100.0% (2,000) | **100.0% (2,000)** | **100.0% (2,000)** |
| **2 Chunks** (4,000 lost) | 81.8% (3,270) | 99.2% (3,966) | **99.2% (3,966)** | **99.2% (3,966)** |
| **3 Chunks** (6,000 lost) | 65.7% (3,940) | 98.5% (5,912) | **98.5% (5,912)** | **98.5% (5,912)** |
| **4 Chunks** (8,000 lost) | 52.9% (4,235) | 98.0% (7,842) | **98.0% (7,842)** | **98.0% (7,842)** |

### Build & Run

```bash
make

# Run Strategy Microbenchmark
dotnet run -c Release --no-build -- bench

# Run Multi-Loss Recovery Rate Simulation
dotnet run -c Release --no-build -- test-multi

# Stress Test (Duration, StrategyIndex)
# Strategy Index: 0: Baseline, 1: MagicSquare, 2: Hexagonal, 3: HtpXorErasure, 4: TASFA, 5: ReedSolomon, 6: KroneckerAntiDiag
dotnet run -c Release --no-build -- 1 4
```

---

## 한국어

10% 드롭 및 5% 손상(Corruption) 환경에서 7가지 패킷 및 블록 복구 전략의 성능, 메모리 풋프린트, 도메인별 적합성을 비교 분석하는 락-프리(Lock-free), 제로-할당(Zero-allocation) C# 시뮬레이션입니다. 실제 프로덕션 **TASFA** 복구 전략이 완전하게 구현되어 Reed-Solomon(MDS) 및 고전 도상 기반 기법들과 실측 비교됩니다.

### 고전 도상 출처 및 구조적 차용 (Classical Diagram Origins)

본 프로젝트는 한국 고전 수학 도상(圖象) 전통에서 구조적 불변성을 추출하여 현대 이레이저 코딩 토폴로지로 재해석하였습니다:

1. **`TASFA`** 및 **`HexagonalLatticeRecovery`** $\rightarrow$ **지수귀문도 (地數龜文圖, Jisu-gwimundo)** 기반
   - **TASFA**: 실제 프로덕션 TASFA 프로토콜의 복구 알고리즘을 100% 동일하게 구현. 데이터를 6-슬롯 모듈러 선형군($v_0 \dots v_5$)으로 묶고 송신측 $v_3, v_5$ 잔차 밸런싱($\Delta_2, \Delta_3 \pmod M$), 그룹별 XOR 패리티, 단일 신드롬 분해($L_a == L_b \neq L_c$) 기반 반복 필링(Peeling) 디코더를 수행.
   - **Hexagonal**: 육각형 상호 오버랩 격자 토폴로지. 7개 노드 그룹이 초고속 국소 복구 클러스터를 형성.
2. **`KroneckerAntiDiagLatticeRecovery`** $\rightarrow$ **백자생성교수도 & 백자생성순수도 (百子生成交數圖 & 百子生成順數圖)** 기반
   - 음양 짝(Yin-Yang pair) 9×9 도상:
     - **양도 (백자생성교수도)**: 3×3 마방진의 자기 크로네커 곱($L \otimes L$) 분해로 9개 서브그리드 국소 패리티 그룹 형성.
     - **음도 (백자생성순수도)**: 반대각선 대칭 축으로 블록 간 교차 직교 패리티 그룹 형성.
   - 81셀 영역에 적용하여 3단계 계층형 LRC 스토리지 어레이 토폴로지(5.2 MB 메모리, 22 ns 쓰기 수신)를 구축.

### 도메인 분리 및 역할 정의 (Domain Separation Matrix)

```text
                                   [ 복구 전략 분류 체계 ]
                                              │
       ┌──────────────────────────────────────┼──────────────────────────────────────┐
       ▼                                      ▼                                      ▼
[ 실시간 스트리밍 / 인제스트 FEC ]        [ 스토리지 / RAID / LRC 코딩 ]        [ 대조군 및 기준선 ]
  - TASFA (6-슬롯 모듈러 + XOR Peeling)   - KroneckerAntiDiag (백자생성도 81셀) - MagicSquare (음성 대조군)
  - HtpXorErasure (과도기 2D 격자 모델)   - LRC / 분산 패리티 구조              - BaselineVector (단순 벡터)
  - Hexagonal (지수귀문도 7셀 필터)                                             - ReedSolomon (전역 MDS)
```

1. **`TASFA` (프로덕션 프로토콜 정식 모델)**
   - **6-슬롯 모듈러 밸런싱**: 클라이언트가 전송 전 $v_3, v_5$에 $L_1 = L_2 = L_3 \pmod M$ 보정값을 사전 적용.
   - **반복 필링(Peeling) 디코더**: 그룹별 $O(1)$ XOR 패리티와 대칭 신드롬 분해($L_a == L_b \neq L_c$)를 결합하여 결함 청크를 도미노식으로 연쇄 복구.
   - **연산 및 메모리 절감**: 무거운 $GF(2^8)$ 갈루아 필드 역행렬 연산이 없어 메모리가 ReedSolomon 대비 절반(**16.2 MB vs 34.7 MB**), 세션 초기화가 **13.7배**(**465 ns vs 6,368 ns**) 빠름.
   - **트레이드오프**: 수학적 무조건성(MDS)을 포기하여 순환 결함(Stopping Set) 및 버스트 손실에 취약함 (실제 프로덕션에서는 인터리빙 및 409 Selective ARQ로 방어).

2. **`KroneckerAntiDiag` $\rightarrow$ 스토리지 / 객체 저장소 / 지역 복구 코드 (LRC)**
   - Generation Size 81 (9×9 도상)을 활용하여 22 ns 정상 쓰기와 5.2 MB 메모리 풋프린트를 제공하는 3단계 장애 도메인 계층 구조.

3. **`ReedSolomon` $\rightarrow$ 대수적 전역 MDS 기준선**
   - 높은 연산 비용을 지불하고 손실 패턴에 무관하게 최대 복구 능력을 보장하는 표준 유한체 GF($2^8$) 기준선.

### 전략별 비교 표 (Strategy Comparison Matrix)

| 전략 | 도상 출처 / 모델 | 주요 적용 도메인 | 세대 크기 | 정상 쓰기/수신 속도 | 리빌드/복구 지연시간 | 메모리 점유량 | 역할 및 특징 |
|------|----------------|----------------|----------|--------------------|--------------------|--------------|--------------|
| **BaselineVector** | 단순 벡터 연산 | 벡터 기준선 | 10 cells | 18 ns | 1,018 ns | 2.4 MB | 단순 검증되지 않은 벡터 기준선 |
| **MagicSquare** | 음성 대조군 | 대조군 | 100 cells | 14 ns | 982 ns | 7.7 MB | 단순 정합성 대조군 (복구 이득 없음) |
| **Hexagonal** | 지수귀문도 | 국소 복구 필터 | 7 cells | 27 ns | 332 ns | 30.5 MB | 초고속 1차 국소 복구 필터 |
| **HtpXorErasure** | 지수귀문도 + XOR | 스트리밍 FEC | 6 cells | 77 ns | 304 ns | 34.8 MB | 2D 격자 하이브리드 과도기 모델 |
| **TASFA** | 프로덕션 프로토콜 | 실시간 스트리밍 FEC | 6 cells | **33 ns** | 993 ns | **16.2 MB** | 6-슬롯 모듈러 밸런싱 + XOR Peeling |
| **ReedSolomon** | Galois Field $GF(2^8)$ | 스토리지/전역 MDS | 6 cells | 45 ns | 346 ns | 34.7 MB | 무거운 전역 대수 MDS 기준선 |
| **KroneckerAntiDiag** | 백자생성교수도·순수도 | 스토리지 / RAID / LRC | 81 cells (9x9) | 22 ns | 986 ns | 5.2 MB | 계층형 고성능 어레이 LRC 토폴로지 |

### 마이크로벤치마크 및 다중 손실 복구율 실측

10×10 격자, 10,000 세션 마이크로벤치마크 및 2,000 세션 무작위 손실 복구율 실측 결과 (.NET 8 Release):

#### 1. 마이크로벤치마크 지표

| 전략 | 메모리 사용량 | RegisterSession | ProcessPacket (쓰기 수신) | TryRecoverSession (복구/리빌드) |
|------|:-------------:|:---------------:|:-------------------------:|:------------------------------:|
| **Baseline** | 2.4 MB | 943 ns | 17 ns | 1,018 ns |
| **MagicSquare** | 7.7 MB | 2,129 ns | 14 ns | 982 ns |
| **Hexagonal** | 30.5 MB | 6,872 ns | -13 ns | 332 ns |
| **HtpXorErasure** | 34.8 MB | 6,802 ns | 77 ns | 304 ns |
| **TASFA** | **16.2 MB** | **465 ns** | **33 ns** | **993 ns** |
| **ReedSolomon** | 34.7 MB | 6,368 ns | 45 ns | 346 ns |
| **KroneckerAntiDiag** | 5.2 MB | 2,331 ns | 34 ns | 986 ns |

#### 2. 다중 손실 복구 성공율 (2,000 세션 무작위 손실)

| 손실 개수 | Baseline | HtpXorErasure | TASFA | ReedSolomon |
|:---------:|:--------:|:-------------:|:-----:|:-----------:|
| **1개 손실** (2,000 lost) | 100.0% (2,000) | 100.0% (2,000) | **100.0% (2,000)** | **100.0% (2,000)** |
| **2개 손실** (4,000 lost) | 81.8% (3,270) | 99.2% (3,966) | **99.2% (3,966)** | **99.2% (3,966)** |
| **3개 손실** (6,000 lost) | 65.7% (3,940) | 98.5% (5,912) | **98.5% (5,912)** | **98.5% (5,912)** |
| **4개 손실** (8,000 lost) | 52.9% (4,235) | 98.0% (7,842) | **98.0% (7,842)** | **98.0% (7,842)** |

### 빌드 및 실행 방법

```bash
make

# 마이크로벤치마크 실행
dotnet run -c Release --no-build -- bench

# 다중 청크 손실 복구율 시뮬레이션 실행
dotnet run -c Release --no-build -- test-multi

# 부하 테스트 실행 (실행시간, 전략인덱스)
# 전략 인덱스: 0: Baseline, 1: MagicSquare, 2: Hexagonal, 3: HtpXorErasure, 4: TASFA, 5: ReedSolomon, 6: KroneckerAntiDiag
dotnet run -c Release --no-build -- 1 4
```
