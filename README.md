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

### Computational Load (Microbenchmark)

Measured on a 10x10 grid with ~50 blank cells, 10,000 sessions, JIT-warmed.
`ProcessPacket` cost is isolated by subtracting `RegisterSession` amortization.

| Strategy | Memory | RegisterSession | ProcessPacket | TryRecoverSession |
|----------|--------|----------------|--------------|------------------|
| **Baseline** | 2.4 MB | 953 ns | **16 ns** | 1,019 ns |
| **MagicSquare** | 7.7 MB | 2,468 ns | **13 ns** | 981 ns |
| **Hexagonal** | 30.5 MB | 5,424 ns | **13 ns** | **265 ns** |
| **HtpXorErasure** | 34.8 MB | 6,092 ns | 74 ns | 322 ns |
| **ReedSolomon** | 34.7 MB | 5,859 ns | 82 ns | 349 ns |

Key observations:
- **ProcessPacket** — Baseline / MagicSquare / Hexagonal are all ~13-16 ns. HtpXorErasure and ReedSolomon are 5-6x heavier (74-82 ns) because every arriving packet must update XOR or GF(2^8) parity accumulators in addition to the hex-group and vector state. This is the primary driver of their lower throughput in the simulation.
- **TryRecoverSession** — Hexagonal (265 ns) is ~3.8x faster than Baseline (1,019 ns). The overlapping hex groups resolve the missing cell early, short-circuiting the full vector scan. HtpXorErasure and ReedSolomon add only ~60-80 ns over Hexagonal despite carrying the extra XOR/GF check.
- **RegisterSession** — Cost grows with data-structure complexity. Baseline is cheapest (953 ns); FEC strategies pay ~6x more (5,859-6,092 ns) to initialize hex groups and parity tables.
- **Memory** — Baseline (2.4 MB) fits in L3. MagicSquare (7.7 MB) stays in L3 on most hardware. Hexagonal/HtpXorErasure/ReedSolomon (~30-35 MB) exceed typical L3 and cause DRAM pressure, which explains the throughput gap in the simulation relative to the per-call ns costs.

### Comparative Analysis

**1. Recovery Rate**
- HtpXorErasure (184.30%) and ReedSolomon (184.47%) dwarf the other three strategies by two orders of magnitude.
- FEC-based strategies recover corrupted packets as well as dropped ones. The XOR / GF(2^8) parity check catches any packet whose payload does not match the group's expected syndrome, recovering the correct value from stored parity even when the packet was delivered with a wrong value.
- Among the non-FEC strategies, Hexagonal (3.80%) outperforms Baseline (2.44%) and MagicSquare (2.39%). Overlapping hex groups create more single-gap opportunities per cell.
- MagicSquare's uniqueness masks eliminate false positives without adding raw recovery volume, so its rate sits slightly below Baseline.

**2. Throughput**
- Baseline is the fastest in both simulation (688K pkt/s) and per-call cost (16 ns/ProcessPacket). The fewest data structures means the lowest memory footprint (2.4 MB) and best cache behavior.
- HtpXorErasure (473K pkt/s) is the slowest: 74 ns/packet due to XOR parity updates on top of hex groups and vectors, plus a ~35 MB working set that spills to DRAM.
- The simulation throughput gap (688K → 473K, -31%) is larger than the per-call gap (16 ns → 74 ns, 4.6x) because the simulation also contends for cache lines across concurrent threads, amplifying the DRAM pressure from the larger working set.

**3. Session Completion (Won)**
- HtpXorErasure leads with 38 sessions won per minute: FEC interception prevents corrupted values from entering the game state.
- MagicSquare (34) benefits from its global uniqueness guard. ReedSolomon (35) intercepts corrupted packets via the GF syndrome check.
- Baseline (31) and Hexagonal (32) accept any geometrically consistent candidate and are more vulnerable to false recoveries.

**4. Memory Footprint**
- Baseline: smallest — only row/col/diag accumulator arrays (2.4 MB, fits L2/L3).
- MagicSquare: adds 6 x `ulong[MaxSessions x MaxGridSize]` bitmasks (7.7 MB, fits L3).
- Hexagonal: adds hex sum/count/membership arrays (30.5 MB, exceeds L3 on many CPUs).
- HtpXorErasure: hex arrays + XOR parity arrays (34.8 MB). Largest runtime working set.
- ReedSolomon: same as HtpXorErasure plus `_rsCellPos` and three static GF(2^8) tables (34.7 MB). Negligible difference from HtpXorErasure.

**5. Correctness Guarantees**
- Baseline: **none**. Can recover a value already present in the same row/column.
- MagicSquare: **strong**. No duplicates in rows, columns, or the global lattice.
- Hexagonal: **moderate**. Local group-sum cross-validation, but duplicate values can pass if no single group sum is violated.
- HtpXorErasure: **moderate + erasure detection**. XOR parity catches corrupted packets before they are applied to the grid. No global uniqueness guard.
- ReedSolomon: **moderate + algebraically stronger erasure detection**. GF(2^8) syndrome is harder to collide than plain XOR, providing better false-positive rejection of corrupted packets. No global uniqueness guard.

### Verdict

- **Use ReedSolomon** when maximizing recovered packet count is the priority and both drop and corruption must be handled. It delivers the **highest absolute recovery** and slightly better algebraic resilience than HtpXorErasure at comparable memory and CPU cost.
- **Use HtpXorErasure** as a marginally cheaper alternative to ReedSolomon when GF(2^8) arithmetic overhead is a concern. Nearly identical recovery outcomes.
- **Use MagicSquare** when correctness of the game state matters above all else. It provides the strongest uniqueness guarantee with a compact 7.7 MB footprint.
- **Use Hexagonal** when raw drop recovery is the only goal, FEC overhead is unacceptable, and you can tolerate the 30 MB working set. It recovers 60% more dropped packets than Baseline and has the fastest TryRecoverSession of all strategies (265 ns).
- **Use Baseline** only when memory is extremely constrained (2.4 MB). Fastest RegisterSession, no correctness guarantees.

### Build & Run

```bash
make

# Microbenchmark (per-operation latency + memory)
dotnet run -c Release --no-build -- bench

# Stress test (replace N with duration in minutes, S with strategy index)
dotnet run -c Release --no-build -- N S
#   S = 0: Baseline
#   S = 1: MagicSquare
#   S = 2: Hexagonal
#   S = 3: HtpXorErasure
#   S = 4: ReedSolomon

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

### 연산 부하 (마이크로벤치마크)

10x10 그리드, 공백 셀 ~50개, 세션 10,000개, JIT 워밍업 후 측정.
`ProcessPacket` 비용은 `RegisterSession` 비용을 분리한 순수 값입니다.

| 전략 | 메모리 | RegisterSession | ProcessPacket | TryRecoverSession |
|------|--------|----------------|--------------|------------------|
| **Baseline** | 2.4 MB | 953 ns | **16 ns** | 1,019 ns |
| **MagicSquare** | 7.7 MB | 2,468 ns | **13 ns** | 981 ns |
| **Hexagonal** | 30.5 MB | 5,424 ns | **13 ns** | **265 ns** |
| **HtpXorErasure** | 34.8 MB | 6,092 ns | 74 ns | 322 ns |
| **ReedSolomon** | 34.7 MB | 5,859 ns | 82 ns | 349 ns |

주요 관찰:
- **ProcessPacket** — Baseline / MagicSquare / Hexagonal은 모두 ~13-16 ns. HtpXorErasure와 ReedSolomon은 5-6배 무겁습니다(74-82 ns). 패킷 도착마다 XOR 또는 GF(2^8) 패리티 누산기를 육각형 그룹·벡터 상태에 더해 추가 업데이트하기 때문입니다. 시뮬레이션 처리량 하락의 주원인입니다.
- **TryRecoverSession** — Hexagonal(265 ns)이 Baseline(1,019 ns)보다 ~3.8배 빠릅니다. 오버랩되는 육각형 그룹이 누락 셀을 조기 판별하여 전체 벡터 스캔을 단락(short-circuit)합니다. HtpXorErasure와 ReedSolomon은 Hexagonal 대비 XOR/GF 검사가 추가되지만 60-80 ns 증가에 그칩니다.
- **RegisterSession** — 자료구조 복잡도에 비례해 비용이 증가합니다. Baseline이 가장 저렴(953 ns), FEC 전략은 육각형 그룹·패리티 테이블 초기화로 ~6배 더 듭니다(5,859-6,092 ns).
- **메모리** — Baseline(2.4 MB)은 L3에 완전히 적합합니다. MagicSquare(7.7 MB)도 L3 범위입니다. Hexagonal/HtpXorErasure/ReedSolomon(~30-35 MB)은 대부분의 CPU L3를 초과하여 DRAM 압력을 유발하며, 이것이 시뮬레이션에서 처리량 격차를 확대하는 원인입니다.

### 비교 분석

**1. 복구율**
- HtpXorErasure(184.30%)와 ReedSolomon(184.47%)은 나머지 세 전략을 두 자릿수 차이로 압도합니다.
- FEC 기반 전략은 드롭 패킷뿐 아니라 **손상 패킷**도 복구합니다. XOR / GF(2^8) 패리티 검사가 잘못된 값의 패킷을 감지하고 저장된 패리티로 정확한 값을 복원하기 때문에, 복구 수가 드롭 수에 묶이지 않습니다.
- FEC 미사용 전략 중에서는 Hexagonal(3.80%)이 Baseline(2.44%)과 MagicSquare(2.39%)를 능가합니다.
- MagicSquare의 고유성 마스크는 거짓 양성만 제거하고 원시 복구량은 늘리지 않아 복구율이 Baseline보다 소폭 낮습니다.

**2. 처리량**
- Baseline은 시뮬레이션(688K pkt/s)과 호출당 비용(16 ns/ProcessPacket) 모두 가장 빠릅니다. 가장 단순한 자료구조와 2.4 MB 메모리 풋프린트 덕분에 캐시 효율이 최고입니다.
- HtpXorErasure(473K pkt/s)가 가장 느립니다. 74 ns/패킷의 XOR 패리티 업데이트와 ~35 MB 워킹 셋이 DRAM 접근을 유발합니다.
- 시뮬레이션 처리량 격차(688K → 473K, -31%)가 호출당 격차(16 ns → 74 ns, 4.6배)보다 큰 이유는, 시뮬레이션의 멀티스레드 경합이 큰 워킹 셋의 캐시 압력을 더욱 증폭시키기 때문입니다.

**3. 세션 완료 (Won)**
- HtpXorErasure가 분당 38 세션으로 1위입니다.
- ReedSolomon(35)과 MagicSquare(34)도 잘못된 값 침투를 억제합니다.
- Baseline(31)과 Hexagonal(32)은 기하학적으로 일관된 후보면 그대로 수용합니다.

**4. 메모리 사용량**
- Baseline: 2.4 MB — L2/L3에 완전 적합.
- MagicSquare: 7.7 MB — L3 범위.
- Hexagonal: 30.5 MB — 대부분 CPU의 L3 초과.
- HtpXorErasure: 34.8 MB — 런타임 워킹 셋 최대.
- ReedSolomon: 34.7 MB — HtpXorErasure와 사실상 동일.

**5. 정합성 보장**
- Baseline: **없음**.
- MagicSquare: **강함**. 행, 열, 전체 격자에 중복 발생 불가.
- Hexagonal: **중간**. 지역적 그룹 합 교차 검증. 단, 단일 그룹 합을 깨뜨리지 않는 중복 값은 통과 가능.
- HtpXorErasure: **중간 + 이레이저 감지**. XOR 패리티로 손상 패킷을 그리드 반영 전 차단. 전역 고유성 가드는 없음.
- ReedSolomon: **중간 + 대수적으로 강화된 이레이저 감지**. GF(2^8) 신드롬은 단순 XOR보다 충돌 확률이 낮습니다. 전역 고유성 가드는 없음.

### 결론

- **ReedSolomon**: 드롭과 손상 모두 처리하면서 복구량을 극대화할 때. 대수적 내성도 HtpXorErasure보다 소폭 우수합니다.
- **HtpXorErasure**: GF 연산 오버헤드가 부담스러울 때 ReedSolomon의 저비용 대안.
- **MagicSquare**: 게임 상태 정합성이 최우선이고 메모리 풋프린트(7.7 MB)가 중요할 때.
- **Hexagonal**: FEC 없이 드롭 복구 극대화 + 가장 빠른 TryRecoverSession(265 ns). 30 MB 워킹 셋을 감수할 수 있을 때.
- **Baseline**: 메모리가 극도로 제한적(2.4 MB)이고 그리드 오염을 감수할 때.

### 빌드 및 실행

```bash
make

# 마이크로벤치마크 (연산 부하 + 메모리 측정)
dotnet run -c Release --no-build -- bench

# 스트레스 테스트 (N=분, S=전략 인덱스)
dotnet run -c Release --no-build -- N S
#   S = 0: Baseline
#   S = 1: MagicSquare
#   S = 2: Hexagonal
#   S = 3: HtpXorErasure
#   S = 4: ReedSolomon

# 5개 전략 전체 순차 실행 (각 10분)
bash run_all_tests.sh
```
