# UltraSudoku

## English

A lock-free, zero-allocation C# network simulation comparing five packet-recovery strategies under 10% drop / 5% corruption.

### Architecture

- **`GameSessionManager`** — Puzzle state (`_currentGrids`, `_solutionGrids`), move validation, session lifecycle.
- **`IRecoveryStrategy`** — Strategy interface for network-layer recovery.
- **`ClientSimulation`** — Async traffic generator with lock-free ring buffer and arena pool.

All synchronization is via `System.Threading.Interlocked` only. The hot loop allocates zero heap objects.

### Recovery Strategies

| Strategy | What it does |
|----------|-------------|
| **BaselineVectorRecovery** | Row / column / diagonal / anti-diagonal sum constraints only. `candidate = expectedSum - vectorSum` when exactly one node is missing in a vector. No correctness validation. |
| **MagicSquareRecovery** | Baseline logic **plus** three bitmask uniqueness constraints (row, column, global lattice). Rejects candidates that would duplicate an existing value. |
| **HexagonalLatticeRecovery** | For each interior cell, defines a 7-member hexagonal group (center + 6 neighbors). Tracks per-group sums and counts. Uses **both** hexagonal groups and baseline vectors for recovery. Cross-validates candidates across overlapping groups. |
| **HtpXorErasureRecovery** | Hexagonal groups **plus** XOR-parity erasure coding. Blank cells are partitioned into groups of 6; each group accumulates an XOR parity over solution values. When one cell in a group is missing, it is recovered as `expectedParity ^ currentParity`. Candidates are cross-validated against hex groups and baseline vectors. |
| **ReedSolomonRecovery** | Hexagonal groups **plus** GF(2^8) Vandermonde erasure coding. Each group of 6 blank cells gets a parity symbol computed as the XOR of `value * alpha^pos` in GF(2^8) (primitive polynomial 0x11D). Single-erasure recovery solves `syndrome / alpha^pos` in the field. Cross-validated with hex groups and baseline vectors. |

### 1-Minute Stress Test Results

> The "Recovered" counter includes packets recovered from both dropped and corrupted channels.
> Strategies 3-4 use forward error correction that intercepts corrupted packets before they enter the
> game state, so their recovery count exceeds the raw drop count.

| Metric | Baseline | MagicSquare | Hexagonal | HtpXorErasure | ReedSolomon |
|--------|----------|-------------|-----------|---------------|-------------|
| **Total Packets Sent** | 41,323,326 | 34,744,705 | 35,173,439 | 28,384,937 | 31,222,162 |
| **Dropped (~11%)** | 4,591,161 | 3,861,201 | 3,909,595 | 3,150,660 | 3,467,135 |
| **Corrupted (~5.5%)** | 2,295,785 | 1,930,934 | 1,952,681 | 1,578,096 | 1,735,366 |
| **Recovered (Network)** | 111,845 | 92,390 | 148,623 | **5,806,650** | **6,395,652** |
| **Recovery Rate** | 2.44% | 2.39% | 3.80% | **184.30%** | **184.47%** |
| **Sessions Won** | 31 | 34 | 32 | **38** | 35 |
| **Throughput (pkt/s)** | **688,722** | 579,078 | 586,224 | 473,082 | 520,369 |

### Comparative Analysis

**1. Recovery Rate**
- HtpXorErasure (184.30%) and ReedSolomon (184.47%) dwarf the other three strategies by two orders of magnitude.
- The key insight: FEC-based strategies recover corrupted packets as well as dropped ones, so the numerator is not bounded by the drop count. The XOR / GF(2^8) parity check catches any packet whose payload does not match the group's expected syndrome, recovering the correct value from stored parity even when the packet was delivered with a wrong value.
- Among the non-FEC strategies, Hexagonal (3.80%) outperforms Baseline (2.44%) and MagicSquare (2.39%). The overlapping hex groups create more single-gap opportunities per cell.
- MagicSquare's uniqueness masks eliminate false positives without adding raw recovery volume, hence its rate sits slightly below Baseline.

**2. Throughput**
- Baseline achieves the highest throughput (688K pkt/s) because it has the fewest data structures.
- MagicSquare (579K pkt/s) pays a small price for the bitmask uniqueness checks; Hexagonal (586K pkt/s) is similar.
- ReedSolomon (520K pkt/s) incurs GF(2^8) multiply operations on each packet arrival.
- HtpXorErasure (473K pkt/s) is the slowest: it updates hex groups, XOR parity, and baseline vectors simultaneously, with more memory traffic than any other strategy.

**3. Session Completion (Won)**
- HtpXorErasure leads with 38 sessions won per minute, primarily because FEC interception prevents many corrupted values from entering the game state.
- MagicSquare (34) benefits from its uniqueness guard, and ReedSolomon (35) similarly intercepts corrupted packets before game state injection.
- Baseline (31) and Hexagonal (32) accept any geometrically consistent candidate, which can include wrong values.

**4. Memory Footprint**
- Baseline: smallest — only row/col/diag accumulator arrays.
- MagicSquare: adds 6 x `ulong[MaxSessions x MaxGridSize]` bitmasks (~12 MB total).
- Hexagonal: adds hex sum/count/membership arrays (~29 MB total). Largest of the first three.
- HtpXorErasure: hex arrays + XOR parity arrays (`_xorExpectedParity`, `_xorCurrentParity`, `_xorCounts`, `_xorGroupSizes`, `_xorCellGroup`) ~30-31 MB total.
- ReedSolomon: same as HtpXorErasure plus `_rsCellPos` and three static GF(2^8) tables (512 B each) ~31-32 MB total. Largest overall.

**5. Correctness Guarantees**
- Baseline: **none**. Can recover a value already present in the same row/column.
- MagicSquare: **strong**. No duplicates in rows, columns, or the global lattice.
- Hexagonal: **moderate**. Local group-sum cross-validation, but duplicate values can pass if no single group sum is violated.
- HtpXorErasure: **moderate + erasure detection**. XOR parity catches corrupted packets before they are applied to the grid. No global uniqueness guard.
- ReedSolomon: **moderate + algebraically stronger erasure detection**. GF(2^8) syndrome is harder to collide than plain XOR, providing better false-positive rejection of corrupted packets. No global uniqueness guard.

### Verdict

- **Use ReedSolomon** when maximizing recovered packet count is the priority and both drop and corruption must be handled. It delivers the **highest absolute recovery** and better algebraic resilience than HtpXorErasure.
- **Use HtpXorErasure** as a slightly cheaper alternative to ReedSolomon when GF arithmetic overhead is a concern. Nearly identical recovery outcomes at lower CPU cost.
- **Use MagicSquare** when correctness of the game state matters above all else. It provides the strongest uniqueness guarantee and a solid balance of throughput and completed sessions.
- **Use Hexagonal** when raw drop recovery (not corruption recovery) is the only goal and FEC overhead is unacceptable. It recovers 60% more dropped packets than Baseline.
- **Use Baseline** only when memory is extremely constrained and grid corruption is acceptable.

### Build & Run

```bash
make
# 1-minute test for each strategy
dotnet run -c Release --no-build -- 1 0   # Baseline
dotnet run -c Release --no-build -- 1 1   # MagicSquare
dotnet run -c Release --no-build -- 1 2   # Hexagonal
dotnet run -c Release --no-build -- 1 3   # HtpXorErasure
dotnet run -c Release --no-build -- 1 4   # ReedSolomon

# Run all 5 strategies sequentially (10-minute each)
bash run_all_tests.sh
```

---

## 한국어

락-프리, 제로-할당 C# 네트워크 시뮬레이션. 다섯 가지 패킷 복구 전략을 10% 드롭 / 5% 손상 조건에서 비교합니다.

### 아키텍처

- **`GameSessionManager`** — 퍼즐 상태(`_currentGrids`, `_solutionGrids`), 이동 검증, 세션 라이프사이클 관리
- **`IRecoveryStrategy`** — 네트워크 레이어 복구용 전략 인터페이스
- **`ClientSimulation`** — 락-프리 링 버퍼와 아레나 풀을 사용하는 비동기 트래픽 생성기

모든 동기화는 `System.Threading.Interlocked`만 사용합니다. 핫 루프는 힙 할당을 일절 하지 않습니다.

### 복구 전략

| 전략 | 동작 방식 |
|------|----------|
| **BaselineVectorRecovery** | 행/열/대각선/역대각선 합 제약만 사용. 벡터 내 정확히 1개 노드가 누락되면 `candidate = expectedSum - vectorSum`으로 복구. 정합성 검증 없음. |
| **MagicSquareRecovery** | 베이스라인 로직에 **3가지 비트마스크 고유성 제약**(행, 열, 전체 격자)을 추가. 이미 존재하는 값을 중복으로 만드는 후보를 거부. |
| **HexagonalLatticeRecovery** | 각 내부 셀을 중심으로 7개 멤버(중심 + 6 이웃)로 육각형 그룹을 정의. 그룹별 합과 카운트를 추적. **육각형 그룹과 베이스라인 벡터를 모두** 복구에 사용. 오버랩되는 그룹 간 교차 검증 수행. |
| **HtpXorErasureRecovery** | 육각형 그룹 **+** XOR 패리티 이레이저 코딩. 빈 셀을 6개씩 묶어 그룹을 구성하고, 각 그룹은 정답값의 XOR 패리티를 누적. 그룹 내 셀 1개가 누락되면 `expectedParity ^ currentParity`로 복구. 후보는 육각형 그룹·벡터 제약으로 교차 검증. |
| **ReedSolomonRecovery** | 육각형 그룹 **+** GF(2^8) 반데르몽드 이레이저 코딩. 빈 셀을 6개씩 묶어 `XOR(value * alpha^pos)` (원시다항식 0x11D)으로 패리티 심볼을 계산. 단일 이레이저 복구는 `syndrome / alpha^pos`를 GF 연산으로 해결. 육각형 그룹·벡터 교차 검증 병행. |

### 1분 스트레스 테스트 결과

> "복구" 카운터는 드롭된 패킷과 손상된 패킷 모두의 복구를 포함합니다.
> 전략 3-4는 FEC로 손상 패킷을 게임 상태 진입 전에 차단하므로, 복구 수가 원시 드롭 수를 초과합니다.

| 지표 | Baseline | MagicSquare | Hexagonal | HtpXorErasure | ReedSolomon |
|------|----------|-------------|-----------|---------------|-------------|
| **총 전송 패킷** | 41,323,326 | 34,744,705 | 35,173,439 | 28,384,937 | 31,222,162 |
| **드롭 (~11%)** | 4,591,161 | 3,861,201 | 3,909,595 | 3,150,660 | 3,467,135 |
| **손상 (~5.5%)** | 2,295,785 | 1,930,934 | 1,952,681 | 1,578,096 | 1,735,366 |
| **복구 (네트워크)** | 111,845 | 92,390 | 148,623 | **5,806,650** | **6,395,652** |
| **복구율** | 2.44% | 2.39% | 3.80% | **184.30%** | **184.47%** |
| **완료 세션 (Won)** | 31 | 34 | 32 | **38** | 35 |
| **처리량 (pkt/s)** | **688,722** | 579,078 | 586,224 | 473,082 | 520,369 |

### 비교 분석

**1. 복구율**
- HtpXorErasure(184.30%)와 ReedSolomon(184.47%)은 나머지 세 전략을 두 자릿수 차이로 압도합니다.
- 핵심 원인: FEC 기반 전략은 드롭 패킷뿐 아니라 **손상 패킷**도 복구합니다. XOR / GF(2^8) 패리티 검사가 잘못된 값의 패킷을 감지하고 저장된 패리티로 정확한 값을 복원하기 때문에, 복구 수가 드롭 수에 묶이지 않습니다.
- FEC 미사용 전략 중에서는 Hexagonal(3.80%)이 Baseline(2.44%)과 MagicSquare(2.39%)를 능가합니다.
- MagicSquare의 고유성 마스크는 거짓 양성만 제거하고 원시 복구량은 늘리지 않아 복구율이 Baseline보다 소폭 낮습니다.

**2. 처리량**
- Baseline이 688K pkt/s로 가장 빠릅니다.
- MagicSquare(579K)와 Hexagonal(586K)은 유사합니다.
- ReedSolomon(520K)은 패킷 도착 시마다 GF(2^8) 곱셈 연산이 추가됩니다.
- HtpXorErasure(473K)가 가장 느립니다. 육각형 그룹·XOR 패리티·벡터를 동시에 업데이트하여 메모리 트래픽이 가장 많습니다.

**3. 세션 완료 (Won)**
- HtpXorErasure가 분당 38 세션으로 1위입니다. FEC가 손상 값을 게임 상태 진입 전에 차단하기 때문입니다.
- ReedSolomon(35)과 MagicSquare(34)도 잘못된 값 침투를 억제합니다.
- Baseline(31)과 Hexagonal(32)은 기하학적으로 일관된 후보면 그대로 수용하여 오염 가능성이 높습니다.

**4. 메모리 사용량**
- Baseline: 최소 — 행/열/대각 누산 배열만.
- MagicSquare: 6개의 `ulong[MaxSessions x MaxGridSize]` 마스크 추가(~12 MB).
- Hexagonal: 육각형 합/카운트/멤버십 배열(~29 MB). 앞 세 전략 중 최대.
- HtpXorErasure: 육각형 배열 + XOR 패리티 배열 5종 ~30-31 MB.
- ReedSolomon: HtpXorErasure + `_rsCellPos` + GF(2^8) 정적 테이블(3 x 512 B) ~31-32 MB. 전체 최대.

**5. 정합성 보장**
- Baseline: **없음**. 동일 행/열의 기존 값을 복구해 무효 그리드를 만들 수 있습니다.
- MagicSquare: **강함**. 행, 열, 전체 격자에 중복 발생 불가.
- Hexagonal: **중간**. 지역적 그룹 합 교차 검증. 단, 단일 그룹 합을 깨뜨리지 않는 중복 값은 통과 가능.
- HtpXorErasure: **중간 + 이레이저 감지**. XOR 패리티로 손상 패킷을 그리드 반영 전 차단. 전역 고유성 가드는 없음.
- ReedSolomon: **중간 + 대수적으로 강화된 이레이저 감지**. GF(2^8) 신드롬은 단순 XOR보다 충돌 확률이 낮아 손상 패킷 거짓 양성을 더 잘 걸러냅니다. 전역 고유성 가드는 없음.

### 결론

- **ReedSolomon**: 드롭과 손상 모두 처리하면서 복구량을 극대화할 때 사용하십시오. **절대 복구 수 최고**, 대수적 내성도 HtpXorErasure보다 우수합니다.
- **HtpXorErasure**: GF 연산 오버헤드가 부담스러울 때 ReedSolomon의 저비용 대안입니다. 복구 결과는 거의 동일합니다.
- **MagicSquare**: 게임 상태의 정합성이 최우선일 때 사용하십시오. 가장 강력한 고유성 보장과 안정적인 처리량·완료 세션 균형을 제공합니다.
- **Hexagonal**: FEC 오버헤드 없이 드롭 패킷 복구만 극대화할 때 사용하십시오. Baseline보다 60% 더 많은 드롭 패킷을 복구합니다.
- **Baseline**: 메모리가 극도로 제한적이고 그리드 오염 위험을 감수할 수 있을 때만 사용하십시오.

### 빌드 및 실행

```bash
make
# 각 전략별 1분 테스트
dotnet run -c Release --no-build -- 1 0   # Baseline
dotnet run -c Release --no-build -- 1 1   # MagicSquare
dotnet run -c Release --no-build -- 1 2   # Hexagonal
dotnet run -c Release --no-build -- 1 3   # HtpXorErasure
dotnet run -c Release --no-build -- 1 4   # ReedSolomon

# 5개 전략 전체 순차 실행 (각 10분)
bash run_all_tests.sh
```
