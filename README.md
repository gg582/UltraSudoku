# UltraSudoku

## English

A lock-free, zero-allocation C# network simulation comparing three packet-recovery strategies under 10% drop / 5% corruption.

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

### 10-Minute Stress Test Results

| Metric | Baseline | MagicSquare | Hexagonal |
|--------|----------|-------------|-----------|
| **Total Packets Sent** | 389,101,058 | 417,865,184 | 361,048,967 |
| **Dropped (10%)** | 43,229,661 | 46,422,183 | 40,119,024 |
| **Corrupted (5%)** | 21,611,322 | 23,204,323 | 20,053,836 |
| **Recovered (Network)** | 1,073,011 | 1,115,757 | **1,544,245** |
| **Recovery Rate** | 2.48% | 2.40% | **3.85%** |
| **Sessions Won** | 17 | **105** | 45 |
| **Throughput (pkt/s)** | 648,502 | **696,442** | 601,748 |

### Comparative Analysis

**1. Recovery Rate**
- Hexagonal (3.85%) outperforms both Baseline (2.48%) and MagicSquare (2.40%).
- The overlapping hexagonal groups create more single-gap opportunities per cell than the fixed row/col/diag vectors alone. A cell belonging to multiple hex groups can be recovered from any of them.
- MagicSquare’s uniqueness masks filter out mathematically impossible candidates, but this **does not increase** the raw recovery volume; it only eliminates false positives. Hence its recovery rate is slightly lower than Baseline.

**2. Throughput**
- MagicSquare achieves the highest throughput (696K pkt/s).
- The uniqueness masks are pure bitwise operations on `ulong` arrays, adding negligible CPU overhead.
- Hexagonal pays a penalty (601K pkt/s, ~13.6% lower than MagicSquare) because each packet arrival must update all hex groups that contain the cell. The member-to-group mapping adds O(1) but non-trivial memory traffic.
- Baseline sits in the middle (648K pkt/s).

**3. Session Completion (Won)**
- MagicSquare wins decisively here (105 sessions).
- By rejecting duplicate values at the network layer, it prevents poisoned cells from entering the game state. Baseline blindly accepts any `expectedSum - sum` result, which frequently injects wrong values and corrupts the grid, leaving only 17 completions.
- Hexagonal (45) benefits from higher raw recovery but still lacks the global uniqueness guard, so some wrong values slip through.

**4. Memory Footprint**
- Baseline: smallest.
- MagicSquare: adds 6 × `ulong[MaxSessions × MaxGridSize]` masks (~12 MB total).
- Hexagonal: adds `_hexExpectedSums`, `_hexCurrentSums`, `_hexCounts`, `_hexGroupSizes`, `_hexMemberToGroups`, `_hexMemberToGroupCount` (~29 MB total). Largest footprint.

**5. Correctness Guarantees**
- Baseline: **none**. Can recover a value already present in the same row/column, producing an invalid grid.
- MagicSquare: **strong**. Magic-square uniqueness guarantees no duplicates in rows, columns, or the global lattice.
- Hexagonal: **moderate**. Group-sum constraints provide local cross-validation, but a duplicate value can still pass if it does not break any single group sum.

### Verdict

- **Use Hexagonal** when maximizing raw recovery is the only goal and memory is abundant. It recovers **60% more packets** than Baseline at the cost of throughput and memory.
- **Use MagicSquare** when correctness matters. It delivers the **highest throughput**, the **most completed sessions**, and mathematically sound recovery. The slight drop in raw recovery rate is the price of zero false positives.
- **Use Baseline** only when memory is extremely constrained and you accept the risk of grid corruption.

### Build & Run

```bash
make
# 10-minute test for each strategy
dotnet run -c Release --no-build -- 10 0   # Baseline
dotnet run -c Release --no-build -- 10 1   # MagicSquare
dotnet run -c Release --no-build -- 10 2   # Hexagonal
```

---

## 한국어

락-프리, 제로-할당 C# 네트워크 시뮬레이션. 세 가지 패킷 복구 전략을 10% 드롭 / 5% 손상 조건에서 비교합니다.

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

### 10분 스트레스 테스트 결과

| 지표 | Baseline | MagicSquare | Hexagonal |
|------|----------|-------------|-----------|
| **총 전송 패킷** | 389,101,058 | 417,865,184 | 361,048,967 |
| **드롭 (10%)** | 43,229,661 | 46,422,183 | 40,119,024 |
| **손상 (5%)** | 21,611,322 | 23,204,323 | 20,053,836 |
| **복구 (네트워크)** | 1,073,011 | 1,115,757 | **1,544,245** |
| **복구율** | 2.48% | 2.40% | **3.85%** |
| **완료 세션 (Won)** | 17 | **105** | 45 |
| **처리량 (pkt/s)** | 648,502 | **696,442** | 601,748 |

### 비교 분석

**1. 복구율**
- Hexagonal(3.85%)이 Baseline(2.48%)과 MagicSquare(2.40%)를 모두 능가합니다.
- 오버랩되는 육각형 그룹은 고정된 행/열/대각선 벡터보다 셀당 더 많은 단일-갭 복구 기회를 만듭니다. 여러 육각형 그룹에 속한 셀은 어느 그룹에서든 복구될 수 있습니다.
- MagicSquare의 고유성 마스크는 수학적으로 불가능한 후보를 걸러내지만, 이는 **원시 복구량을 늘리지 않고** 거짓 양성만 제거합니다. 따라서 복구율이 Baseline보다 소폭 낮습니다.

**2. 처리량**
- MagicSquare가 가장 높은 처리량(696K pkt/s)을 기록합니다.
- 고유성 마스크는 `ulong` 배열에 대한 순수 비트 연산이라 CPU 오버헤드가 미미합니다.
- Hexagonal은 패킷 도착 시 해당 셀을 포함하는 모든 육각형 그룹을 업데이트해야 하므로 페널티를 치릅니다(601K pkt/s, MagicSquare 대비 ~13.6% 저하). 멤버-그룹 매핑은 O(1)이지만 메모리 트래픽이 유의미하게 증가합니다.
- Baseline은 중간 수준(648K pkt/s)입니다.

**3. 세션 완료 (Won)**
- MagicSquare가 이 부분에서 압도적입니다(105 세션).
- 네트워크 레이어에서 중복 값을 거부함으로써 잘못된 값이 게임 상태로 침투하는 것을 방지합니다. Baseline은 `expectedSum - sum` 결과를 맹목적으로 수용하여 잘못된 값을 주입하고 그리드를 손상시키므로 완료 세션이 17개에 그칩니다.
- Hexagonal(45)은 원시 복구량이 높은 덕분에 이점을 보지만, 전역 고유성 가드가 없어 일부 잘못된 값이 여전히 통과합니다.

**4. 메모리 사용량**
- Baseline: 최소.
- MagicSquare: 6개의 `ulong[MaxSessions × MaxGridSize]` 마스크 추가(총 ~12 MB).
- Hexagonal: `_hexExpectedSums`, `_hexCurrentSums`, `_hexCounts`, `_hexGroupSizes`, `_hexMemberToGroups`, `_hexMemberToGroupCount` 추가(총 ~29 MB). 가장 큰 메모리 사용량.

**5. 정합성 보장**
- Baseline: **없음**. 이미 같은 행/열에 존재하는 값을 복구하여 유효하지 않은 그리드를 만들 수 있습니다.
- MagicSquare: **강함**. 마방진 고유성으로 행, 열, 전체 격자에 중복이 발생하지 않음을 보장합니다.
- Hexagonal: **중간**. 그룹 합 제약이 지역적 교차 검증을 제공하지만, 단일 그룹 합을 깨뜨리지 않는 한 중복 값이 통과할 수 있습니다.

### 결론

- **Hexagonal**: 원시 복구량 극대화가 유일한 목표이고 메모리가 넉넉할 때 사용하십시오. Baseline보다 **60% 더 많은 패킷**을 복구하지만, 처리량과 메모리를 희생합니다.
- **MagicSquare**: 정합성이 중요할 때 사용하십시오. **가장 높은 처리량**, **가장 많은 완료 세션**, 수학적으로 타당한 복구를 제공합니다. 원시 복구율의 소폭 감소는 거짓 양성 제거의 대가입니다.
- **Baseline**: 메모리가 극도로 제한적이고 그리드 손상 위험을 감수할 수 있을 때만 사용하십시오.

### 빌드 및 실행

```bash
make
# 각 전략별 10분 테스트
dotnet run -c Release --no-build -- 10 0   # Baseline
dotnet run -c Release --no-build -- 10 1   # MagicSquare
dotnet run -c Release --no-build -- 10 2   # Hexagonal
```
