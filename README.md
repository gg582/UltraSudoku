# UltraSudoku

## English

A lock-free, zero-allocation C# network simulation comparing six packet-recovery strategies under 10% drop / 5% corruption scenarios.

### High-RTT Perspective: First-Line Local Repair Topology

In High-RTT environments (e.g., 200 ms RTT), a decoder latency of 998 ns vs 370 ns represents a negligible difference ($998\text{ ns} = 0.000998\text{ ms} \ll 200\text{ ms}$). The decisive metric is **whether an extra RTT penalty is avoided through local reconstruction**.

The `KroneckerAntiDiag` topology functions as an ideal **First-Line Local Repair Layer**:
- **Ingest Latency**: **22 ns** (pure bitwise XOR without GF(2^8) math).
- **Memory Footprint**: **5.2 MB** (fully cache-resident, vs 34.7 MB for RS).
- **Architecture**: `Kronecker Local Repair -> Stronger FEC / RS -> NACK / Retransmission`.

### Architecture & Components

- **`GameSessionManager`** — Puzzle state (`_currentGrid`, `_solutionGrid`), move validation, session lifecycle.
- **`IRecoveryStrategy`** — Strategy interface for network-layer packet recovery.
- **`ClientSimulation`** — Async traffic generator with lock-free ring buffer and arena pool.

### Recovery Strategies

| Strategy | Description & Topology |
|----------|-----------------------|
| **BaselineVectorRecovery** | Row / column / diagonal / anti-diagonal sum constraints. Recovers when exactly one node is missing in a vector. |
| **MagicSquareRecovery** | Baseline logic **plus** 3 bitmask uniqueness constraints. Serves as a Negative Control. |
| **HexagonalLatticeRecovery** | Overlapping 7-member interior hexagonal groups (center + 6 neighbors). Fast local repair (276 ns). |
| **HtpXorErasureRecovery** | Hexagonal groups **plus** XOR-parity erasure coding (6-cell partition groups). |
| **ReedSolomonRecovery** | Hexagonal groups **plus** GF(2^8) Vandermonde erasure coding (`⊕ value * alpha^pos`). |
| **KroneckerAntiDiagLatticeRecovery** | **First-Line Repair Topology**: 3x3 Kronecker palace decomposition + Anti-diagonal symmetry groups weighted XOR parity. |

### Microbenchmark & High-RTT Local Avoidance

Measured on a 10x10 grid with 10,000 sessions (JIT-warmed):

| Strategy | Memory | RegisterSession | ProcessPacket | TryRecoverSession | Retransmissions Avoided |
|----------|--------|----------------|--------------|------------------|------------------------|
| **Baseline** | 2.4 MB | 1,043 ns | 18 ns | 1,037 ns | Low (Single-gap vector only) |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns | Low (Negative Control) |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** | Fast Local Filter |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns | High (Local XOR) |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns | High (GF(2^8) Heavy Ingest) |
| **KroneckerAntiDiag** | **5.2 MB** | 2,394 ns | **22 ns** | 998 ns | **High (22 ns Fast Ingest)** |

### High-RTT Verification Roadmap

1. **Retransmissions Avoided per 10,000 Lost Chunks**: Tracking local repair rate before RTT NACK generation.
2. **Mean Extra RTTs per Completed Object**: Evaluating end-to-end delivery latency impact.
3. **Temporal Interleaving against Burst Loss**: Decoupling logical 3x3 palace locality from temporal wire-level sequence.

---

## 한국어

10% 드롭 및 5% 패킷 손상(Corruption) 환경에서 6가지 네트워크 패킷 복구 전략의 성능, 메모리 풋프린트, 연산 부하 트레이드오프를 비교 분석하는 락-프리(Lock-free), 제로-할당(Zero-allocation) C# 네트워크 시뮬레이션입니다.

### High-RTT 관점 분석: 1차 현지 복구 토폴로지 (First-Line Local Repair)

200 ms 수준의 High-RTT 환경에서는 디코더 연산 시간(998 ns vs 370 ns)의 차이가 무의미합니다 ($998\text{ ns} = 0.000998\text{ ms} \ll 200\text{ ms}$). 핵심 평가 지표는 **재전송 RTT 발생 자체를 현지에서 미리 방지했는가**입니다.

`KroneckerAntiDiag` 토폴로지는 최적의 **1차 현지 복구 레이어 (First-Line Repair Layer)** 역할을 수행합니다:
- **수신 처리 속도**: 패킷당 **22 ns** (GF(2^8) 연산 없는 pure bitwise XOR).
- **메모리 점유**: **5.2 MB** (L3 캐시 내 전재 적재 가능, RS 34.7 MB 대비 85% 절감).
- **계층형 구조**: `Kronecker 현지 복구 -> 강한 FEC / RS -> NACK / 재전송 요청`.

### 복구 전략 (Recovery Strategies)

| 전략 | 토폴로지 및 동작 원리 |
|------|----------------------|
| **BaselineVectorRecovery** | 행 / 열 / 대각선 / 역대각선 합 제약 조건. |
| **MagicSquareRecovery** | 베이스라인 + 3중 비트마스크 고유성 제약 조건 (Negative Control). |
| **HexagonalLatticeRecovery** | 7개 멤버 오버랩 육각형 그룹 (276 ns 초고속 복구). |
| **HtpXorErasureRecovery** | 육각형 그룹 + 6셀 분할 XOR 패리티 이레이저 코딩. |
| **ReedSolomonRecovery** | 육각형 그룹 + GF(2^8) 반데르몽드 이레이저 코딩. |
| **KroneckerAntiDiagLatticeRecovery** | **1차 현지 복구 토폴로지**: 3×3 궁 크로네커 곱 분해 + 반대각 직교 대칭 가중 XOR 패리티. |

### 마이크로벤치마크 및 High-RTT 성능 측정

10×10 격자, 10,000 세션 조건 (JIT 워밍업 적용):

| 전략 | 메모리 사용량 | RegisterSession | ProcessPacket | TryRecoverSession | 재전송 회피 특성 |
|------|-------------|----------------|--------------|------------------|------------------|
| **Baseline** | 2.4 MB | 1,043 ns | 18 ns | 1,037 ns | 낮음 (단일 결손만 복구) |
| **MagicSquare** | 7.7 MB | 2,203 ns | 17 ns | 1,039 ns | 낮음 (Negative Control) |
| **Hexagonal** | 30.5 MB | 6,343 ns | 27 ns | **276 ns** | 초고속 1차 필터 |
| **HtpXorErasure** | 34.8 MB | 6,415 ns | 67 ns | 350 ns | 높음 (국소 XOR 복구) |
| **ReedSolomon** | 34.7 MB | 6,021 ns | 78 ns | 370 ns | 높음 (수신 시 무거운 GF 연산) |
| **KroneckerAntiDiag** | **5.2 MB** | 2,394 ns | **22 ns** | 998 ns | **높음 (22 ns 초고속 수신)** |

### High-RTT 검증 로드맵

1. **10,000개 손실 청크당 현지 재전송 회피 수 (Retransmissions Avoided)**
2. **완료 오브젝트당 평균 추가 RTT 지연 (Mean Extra RTTs per Object)**
3. **버스트 손실 방지를 위한 인터리빙(Interleaving) 기법 검증**

### 빌드 및 실행 방법

```bash
make
dotnet run -c Release --no-build -- bench
dotnet run -c Release --no-build -- 1 5
```
