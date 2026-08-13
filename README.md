# UltraSudoku

## English

A lock-free, zero-allocation C# simulation comparing six packet-and-block recovery strategies across network streaming and storage array domains under 10% drop / 5% corruption scenarios.

### Classical Diagram Origins & Topological Exaptation

This project extracts structural invariants from traditional Korean mathematical and magic diagram traditions (도상, 圖象) and adapts them into modern erasure coding topologies:

1. **`HexagonalLatticeRecovery`** $\rightarrow$ Derived from **Jisu-gwimundo (지수귀문도, 地數龜文圖)**
   - Hexagonal lattice topology where overlapping 7-node hexagonal groups form ultra-fast local repair clusters (276 ns).
2. **`KroneckerAntiDiagLatticeRecovery`** $\rightarrow$ Derived from **Baekja-saengseong-gyosudo & Baekja-saengseong-sunsudo (백자생성교수도 & 백자생성순수도, 百子生成交數圖 & 百子生成順數圖)**
   - Paired Yin-Yang 9x9 diagrams (음양 짝):
     - **Yang Diagram (백자생성교수도)**: 3x3 Kronecker palace hierarchy ($L \otimes L$) forming 9 local palace parity groups.
     - **Yin Diagram (백자생성순수도)**: Anti-diagonal symmetry groups forming orthogonal cross-palace parity groups.
   - Applied to the 81-cell core region, creating a 3-tier LRC storage array topology (5.2 MB memory, 22 ns write ingest).

### Domain Separation & Categorization Matrix

Rather than forcing all historical diagram topologies into a single network FEC use-case, our empirical findings reveal distinct domain suitability based on generation size, decoding latency, and structural locality:

```text
                                  [ Recovery Strategies ]
                                             │
      ┌──────────────────────────────────────┼──────────────────────────────────────┐
      ▼                                      ▼                                      ▼
[ Streaming / High-RTT FEC ]        [ Storage / RAID / LRC ]              [ Controls & Baselines ]
  - HtpXorErasure (6-cell XOR)        - KroneckerAntiDiag (81-cell 9x9)     - MagicSquare (Negative Control)
  - Hexagonal (Jisu-gwimundo 276 ns)  - LRC / Distributed Parity            - BaselineVector / ReedSolomon (MDS)
```

1. **`HtpXorErasure` & `Hexagonal` (Jisu-gwimundo, 지수귀문도) $\rightarrow$ Streaming & High-RTT Network FEC**
   - Small generation size (6-cell XOR partition). Rapid accumulation allows low-latency online streaming recovery without long framing delays.

2. **`KroneckerAntiDiag` (Baekja-saengseong-gyosudo & Baekja-saengseong-sunsudo, 백자생성교수도 & 백자생성순수도) $\rightarrow$ Storage / Object Store / Local Reconstruction Codes (LRC)**
   - **Generation size 81 (9x9 diagram)** creates a decoding delay bottleneck in real-time streaming, but becomes a **hierarchical advantage for storage arrays**.
   - **Asymmetric Asynchronous Cost**: `22 ns Write/Ingest Process` vs `998 ns Rebuild/Repair`. In RAID/LRC, normal writes/reads dominate ($>99\%$), making ultra-cheap normal-path updates (22 ns, 5.2 MB) ideal for storage controllers.
   - **3-Tier Failure Domain Hierarchy**:
     $$\text{3x3 Palace Repair (교수도)} \longrightarrow \text{Anti-Diagonal Cross Repair (순수도)} \longrightarrow \text{Global Parity Recovery}$$
   - **Physical Placement Rule**: Logical $3\times3$ neighborhood $\neq$ Same physical failure domain. Logical cells are interleaved across physical drives/nodes to guarantee drive-loss tolerance.

3. **`MagicSquare` $\rightarrow$ Negative Control**
   - Proves that grid regularity or number patterns alone yield zero recovery advantage ($2.39\%$ vs $2.44\%$ baseline).

4. **`ReedSolomon` $\rightarrow$ Algebraic Global MDS Baseline**
   - Standard Galois Field GF($2^8$) benchmark representing maximum algebraic recovery at higher CPU/memory costs.

### Strategy Comparison Matrix

| Strategy | Classical Origin (도상) | Primary Domain | Generation Size | Normal Write/Process | Rebuild/Repair Latency | Memory Footprint | Role & Characteristics |
|----------|-----------------------|----------------|-----------------|----------------------|------------------------|------------------|-----------------------|
| **BaselineVector** | — | Vector Math | 10 cells | 18 ns | 1,037 ns | 2.4 MB | Simple Unvalidated Vector Baseline |
| **MagicSquare** | — | Negative Control | 100 cells | 17 ns | 1,039 ns | 7.7 MB | Regularity Control (Unused) |
| **Hexagonal** | Jisu-gwimundo (지수귀문도) | Local Repair Filter | 7 cells | 27 ns | **276 ns** | 30.5 MB | Ultra-Fast 1st-Line Local Repair |
| **HtpXorErasure** | Jisu-gwimundo (지수귀문도) | Streaming Network FEC | 6 cells | 67 ns | 350 ns | 34.8 MB | Online High-RTT Streaming FEC |
| **ReedSolomon** | — | Global MDS Code | 6 cells | 78 ns | 370 ns | 34.7 MB | Heavy Global Algebraic MDS Baseline |
| **KroneckerAntiDiag** | Baekja-saengseong-gyosudo & Sunsudo (백자생성교수도 & 순수도) | Storage / RAID / LRC | 81 cells (9x9) | **22 ns** | 998 ns | **5.2 MB** | Hierarchical High-Throughput Array LRC |

### Microbenchmark & Performance Verification

Measured on a 10x10 grid with 10,000 sessions (JIT-warmed):

| Strategy | Memory | RegisterSession | ProcessPacket (Write Ingest) | TryRecoverSession (Rebuild) | Domain Suitability Evaluation |
|----------|--------|----------------|------------------------------|-----------------------------|------------------------------|
| **Baseline** | 2.4 MB | 1,043 ns | 18 ns | 1,037 ns | Unvalidated Simple Vector |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns | Unused (Negative Control) |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** | Ultra-Fast 1st-Line Local Filter |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns | Real-Time Online Streaming FEC |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns | Heavy Global Algebraic MDS |
| **KroneckerAntiDiag** | **5.2 MB** | 2,394 ns | **22 ns** | 998 ns | **High-Throughput Storage Array** |

### Storage Array & Storage-Domain Roadmap

For storage domain validation (`KroneckerAntiDiag`), the critical evaluation metrics shift to:
1. **Average Blocks Read to Repair 1 Missing Block** (LRC Efficiency Metric).
2. **Rebuild Bandwidth & Degraded-Read Latency** under single-drive vs dual-drive failures.
3. **Physical Interleaving Mapping**: Mapping logical $9\times 9$ cells across $N$ physical drives to prevent co-located failure domain loss.

### Build & Run

```bash
make

# Microbenchmark
dotnet run -c Release --no-build -- bench

# Stress Test (Duration, StrategyIndex)
# Strategy Index: 0: Baseline, 1: MagicSquare, 2: Hexagonal, 3: HtpXorErasure, 4: ReedSolomon, 5: KroneckerAntiDiag
dotnet run -c Release --no-build -- 1 5
```

---

## 한국어

10% 드롭 및 5% 손상(Corruption) 환경에서 6가지 패킷 및 블록 복구 전략의 성능, 메모리 풋프린트, 도메인별 적합성을 비교 분석하는 락-프리(Lock-free), 제로-할당(Zero-allocation) C# 시뮬레이션입니다.

### 고전 도상 출처 및 구조적 차용 (Classical Diagram Origins)

본 프로젝트는 한국 고전 수학 도상(圖象) 전통에서 구조적 불변성을 추출하여 현대 이레이저 코딩 토폴로지로 재해석하였습니다:

1. **`HexagonalLatticeRecovery`** $\rightarrow$ **지수귀문도 (地數龜文圖, Jisu-gwimundo)** 기반
   - 육각형 상호 오버랩 격자 토폴로지. 7개 노드 그룹이 초고속 국소 복구 클러스터(276 ns)를 형성.
2. **`KroneckerAntiDiagLatticeRecovery`** $\rightarrow$ **백자생성교수도 & 백자생성순수도 (百子生成交數圖 & 百子生成順數圖, Baekja-saengseong-gyosudo & Baekja-saengseong-sunsudo)** 기반
   - 음양 짝(Yin-Yang pair) 9×9 도상:
     - **양도 (백자생성교수도)**: 낙서의 3×3 자기 크로네커 곱($L \otimes L$) 분해로 9개 궁(Palace) 국소 패리티 그룹 형성.
     - **음도 (백자생성순수도)**: 반대각선 대칭 축으로 궁 간 교차 직교(Cross-palace orthogonal) 패리티 그룹 형성.
   - 81셀 영역에 적용하여 3단계 계층형 LRC 스토리지 어레이 토폴로지(5.2 MB 메모리, 22 ns 쓰기 수신)를 구축함.

### 도메인 분리 및 역할 정의 (Domain Separation Matrix)

모든 고전 도상 구조를 하나의 네트워크 FEC 용도로 억지로 맞추는 대신, 세대 크기(Generation Size), 복구 지연시간, 공간적 국소성을 기준으로 도메인을 명확히 분리하였습니다:

```text
                                  [ 복구 전략 분류 체계 ]
                                             │
      ┌──────────────────────────────────────┼──────────────────────────────────────┐
      ▼                                      ▼                                      ▼
[ 스트리밍 / High-RTT 네트워크 FEC ]    [ 스토리지 / RAID / LRC 코딩 ]        [ 대조군 및 기준선 ]
  - HtpXorErasure (지수귀문도 6셀)     - KroneckerAntiDiag (백자생성도 81셀) - MagicSquare (음성 대조군)
  - Hexagonal (지수귀문도 276 ns)      - LRC / 분산 패리티 구조             - Baseline / ReedSolomon (MDS)
```

1. **`HtpXorErasure` & `Hexagonal` (지수귀문도, Jisu-gwimundo) $\rightarrow$ 스트리밍 / High-RTT 네트워크 FEC**
   - 작은 세대 크기 (6셀 분할 XOR 그룹). 프레이밍 지연 없는 빠른 누적으로 실시간 온라인 스트리밍 패킷 복구에 적합.

2. **`KroneckerAntiDiag` (백자생성교수도 & 백자생성순수도, Baekja-saengseong-gyosudo & Baekja-saengseong-sunsudo) $\rightarrow$ 스토리지 / 객체 저장소 / 지역 복구 코드 (LRC)**
   - **Generation Size 81 (9×9 도상)**: 실시간 스트리밍에서는 축적 지연이 발생하지만, **스토리지 어레이(RAID/LRC)에서는 계층적 구조의 이점**이 됨.
   - **비대칭 비동기 비용**: `22 ns 정상 쓰기(Ingest)` vs `998 ns 장애 복구(Rebuild)`. 정상 입출력이 99% 이상인 저장장치 특성상 22 ns / 5.2 MB의 캐시 친화적 정상 경로가 극히 유리함.
   - **3단계 장애 도메인 계층**:
     $$\text{3×3 궁 현지 복구 (백자생성교수도)} \longrightarrow \text{반대각 교차 그룹 복구 (백자생성순수도)} \longrightarrow \text{전역 패리티 복구}$$
   - **물리적 분산 배치 규칙**: 논리적 3×3 인접성이 동일한 물리 드라이브/노드에 배치되지 않도록 인터리빙하여 드라이브 유실 내성 확보.

3. **`MagicSquare` $\rightarrow$ 음성 대조군 (Negative Control)**
   - 단순 방진 정합성이나 수치 규칙만으로는 복구 이득이 전혀 없음(2.39% vs 2.44%)을 증명.

4. **`ReedSolomon` $\rightarrow$ 대수적 전역 MDS 기준선**
   - 높은 CPU/메모리 비용을 지불하고 최대 대수적 복구 능력을 제공하는 유한체 GF($2^8$) 기준선.

### 전략별 비교 표 (Strategy Comparison Matrix)

| 전략 | 고전 도상 출처 | 주요 적용 도메인 | 세대 크기 | 정상 쓰기/수신 속도 | 리빌드/복구 지연시간 | 메모리 점유량 | 역할 및 특징 |
|------|--------------|----------------|----------|--------------------|--------------------|--------------|--------------|
| **BaselineVector** | — | 벡터 합 수학 | 10 cells | 18 ns | 1,037 ns | 2.4 MB | 단순 검증되지 않은 벡터 기준선 |
| **MagicSquare** | — | 음성 대조군 | 100 cells | 17 ns | 1,039 ns | 7.7 MB | 단순 정합성 대조군 (미사용) |
| **Hexagonal** | 지수귀문도 (Jisu-gwimundo) | 국소 복구 필터 | 7 cells | 27 ns | **276 ns** | 30.5 MB | 초고속 1차 현지 복구 필터 |
| **HtpXorErasure** | 지수귀문도 (Jisu-gwimundo) | 스트리밍 네트워크 FEC | 6 cells | 67 ns | 350 ns | 34.8 MB | 실시간 온라인 스트리밍 FEC |
| **ReedSolomon** | — | 전역 MDS 코딩 | 6 cells | 78 ns | 370 ns | 34.7 MB | 무거운 전역 대수 MDS 기준선 |
| **KroneckerAntiDiag** | 백자생성교수도 & 백자생성순수도 (Baekja-saengseong) | 스토리지 / RAID / LRC | 81 cells (9x9) | **22 ns** | 998 ns | **5.2 MB** | 계층형 고성능 어레이 LRC |

### 마이크로벤치마크 및 성능 검증 결과

10×10 격자, 10,000 세션 조건 (JIT 워밍업 적용):

| 전략 | 메모리 사용량 | RegisterSession | ProcessPacket (쓰기 수신) | TryRecoverSession (복구/리빌드) | 도메인 적합성 평가 |
|------|-------------|----------------|--------------------------|-------------------------------|-------------------|
| **Baseline** | 2.4 MB | 1,043 ns | 18 ns | 1,037 ns | 검증되지 않은 단순 벡터 |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns | 미사용 (음성 대조군) |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** | 초고속 1차 국소 필터 |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns | 실시간 온라인 스트리밍 FEC |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns | 무거운 전역 대수 MDS |
| **KroneckerAntiDiag** | **5.2 MB** | 2,394 ns | **22 ns** | 998 ns | **고성능 스토리지 어레이** |

### 스토리지 어레이 검증 로드맵 (Storage-Domain Roadmap)

스토리지 도메인 검증(`KroneckerAntiDiag`)을 위한 핵심 평가 지표:
1. **단일 누락 블록 복구당 평균 읽기 블록 수 (Average Blocks Read to Repair 1 Missing Block)**: LRC 코딩 효율성 지표.
2. **리빌드 대역폭 및 성능 저하 읽기 지연시간 (Rebuild Bandwidth & Degraded-Read Latency)**: 단일 드라이브 vs 다중 드라이브 장애 조건.
3. **물리적 인터리빙 매핑 (Physical Interleaving Mapping)**: 논리적 9×9 셀을 N개의 물리 드라이브에 분산시켜 동일 장애 도메인 손실 방지.

### 빌드 및 실행 방법

```bash
make

# 마이크로벤치마크 실행
dotnet run -c Release --no-build -- bench

# 부하 테스트 실행 (실행시간, 전략인덱스)
# 인덱스: 0: Baseline, 1: MagicSquare, 2: Hexagonal, 3: HtpXorErasure, 4: ReedSolomon, 5: KroneckerAntiDiag
dotnet run -c Release --no-build -- 1 5
```
