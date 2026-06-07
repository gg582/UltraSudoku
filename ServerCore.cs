using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSudoku
{
    public struct MovePacket
    {
        public uint SessionId;
        public byte Row;
        public byte Col;
        public byte Value;
        public uint Sequence;
    }

    public static class PacketSerializer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WritePacket(Span<byte> buffer, MovePacket packet)
        {
            buffer[0] = (byte)(packet.SessionId);
            buffer[1] = (byte)(packet.SessionId >> 8);
            buffer[2] = (byte)(packet.SessionId >> 16);
            buffer[3] = (byte)(packet.SessionId >> 24);
            buffer[4] = packet.Row;
            buffer[5] = packet.Col;
            buffer[6] = packet.Value;
            buffer[7] = 0;
            buffer[8] = (byte)(packet.Sequence);
            buffer[9] = (byte)(packet.Sequence >> 8);
            buffer[10] = (byte)(packet.Sequence >> 16);
            buffer[11] = (byte)(packet.Sequence >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MovePacket ReadPacket(ReadOnlySpan<byte> buffer)
        {
            return new MovePacket
            {
                SessionId = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24)),
                Row = buffer[4],
                Col = buffer[5],
                Value = buffer[6],
                Sequence = (uint)(buffer[8] | (buffer[9] << 8) | (buffer[10] << 16) | (buffer[11] << 24))
            };
        }
    }

    public sealed class GridArenaPool
    {
        private readonly byte[] _arena;
        private readonly int _chunkByteSize;
        private readonly int _chunkCount;
        private readonly long[] _allocationMasks;
        private readonly int[] _routingSequence;
        private long _freeCount;

        public GridArenaPool(int chunkByteSize, int chunkCount)
        {
            _chunkByteSize = chunkByteSize;
            _chunkCount = chunkCount;
            _arena = new byte[chunkCount * chunkByteSize];
            int maskCount = (chunkCount + 63) >> 6;
            _allocationMasks = new long[maskCount];
            for (int i = 0; i < maskCount; i++)
            {
                _allocationMasks[i] = -1L;
            }
            _routingSequence = new int[chunkCount];
            int coprime = 7;
            for (int i = 0; i < chunkCount; i++)
            {
                _routingSequence[i] = (i * coprime) % chunkCount;
            }
            _freeCount = chunkCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetChunkSpan(int chunkIndex)
        {
            return new Span<byte>(_arena, chunkIndex * _chunkByteSize, _chunkByteSize);
        }

        public int LeaseChunk()
        {
            if (Interlocked.Read(ref _freeCount) == 0)
            {
                return -1;
            }
            for (int i = 0; i < _routingSequence.Length; i++)
            {
                int chunkIndex = _routingSequence[i];
                int maskIndex = chunkIndex >> 6;
                int bitIndex = chunkIndex & 63;
                long bit = 1L << bitIndex;
                long currentMask;
                long newMask;
                do
                {
                    currentMask = _allocationMasks[maskIndex];
                    if ((currentMask & bit) == 0)
                    {
                        break;
                    }
                    newMask = currentMask & ~bit;
                }
                while (Interlocked.CompareExchange(ref _allocationMasks[maskIndex], newMask, currentMask) != currentMask);
                if ((currentMask & bit) != 0)
                {
                    Interlocked.Decrement(ref _freeCount);
                    return chunkIndex;
                }
            }
            return -1;
        }

        public void ReleaseChunk(int chunkIndex)
        {
            int maskIndex = chunkIndex >> 6;
            int bitIndex = chunkIndex & 63;
            long bit = 1L << bitIndex;
            Interlocked.Or(ref _allocationMasks[maskIndex], bit);
            Interlocked.Increment(ref _freeCount);
        }
    }

    public interface IRecoveryStrategy
    {
        void RegisterSession(int slotIndex, uint sessionId, int gridSize, uint expectedSum, ReadOnlySpan<byte> currentGrid, ReadOnlySpan<byte> solutionGrid);
        void ProcessPacket(int slotIndex, MovePacket packet);
        int TryRecoverSession(int slotIndex, Span<MovePacket> outputBuffer);
    }

    public sealed class BaselineVectorRecovery : IRecoveryStrategy
    {
        public const int MaxSessions = 16384;
        public const int MaxGridSize = 10;

        private readonly uint[] _expectedSums;
        private readonly int[] _gridSizes;
        private readonly uint[] _sessionIdMap;
        private readonly ulong[] _blankCellMaskLow;
        private readonly ulong[] _blankCellMaskHigh;
        private readonly ulong[] _arrivedMaskLow;
        private readonly ulong[] _arrivedMaskHigh;
        private readonly uint[] _rowSums;
        private readonly uint[] _colSums;
        private readonly uint[] _diagSums;
        private readonly uint[] _antiDiagSums;
        private readonly byte[] _rowCounts;
        private readonly byte[] _colCounts;
        private readonly byte[] _diagCounts;
        private readonly byte[] _antiDiagCounts;

        public BaselineVectorRecovery()
        {
            int s = MaxSessions;
            int v = MaxSessions * MaxGridSize;
            _expectedSums = new uint[s];
            _gridSizes = new int[s];
            _sessionIdMap = new uint[s];
            _blankCellMaskLow = new ulong[s];
            _blankCellMaskHigh = new ulong[s];
            _arrivedMaskLow = new ulong[s];
            _arrivedMaskHigh = new ulong[s];
            _rowSums = new uint[v];
            _colSums = new uint[v];
            _diagSums = new uint[s];
            _antiDiagSums = new uint[s];
            _rowCounts = new byte[v];
            _colCounts = new byte[v];
            _diagCounts = new byte[s];
            _antiDiagCounts = new byte[s];
        }

        public void RegisterSession(int slotIndex, uint sessionId, int gridSize, uint expectedSum, ReadOnlySpan<byte> currentGrid, ReadOnlySpan<byte> solutionGrid)
        {
            _sessionIdMap[slotIndex] = sessionId;
            _expectedSums[slotIndex] = expectedSum;
            _gridSizes[slotIndex] = gridSize;
            _blankCellMaskLow[slotIndex] = 0;
            _blankCellMaskHigh[slotIndex] = 0;
            _arrivedMaskLow[slotIndex] = 0;
            _arrivedMaskHigh[slotIndex] = 0;
            int cells = gridSize * gridSize;
            for (int i = 0; i < cells; i++)
            {
                if (currentGrid[i] == 0)
                {
                    if (i < 64)
                        _blankCellMaskLow[slotIndex] |= (1UL << i);
                    else
                        _blankCellMaskHigh[slotIndex] |= (1UL << (i - 64));
                }
            }
            int vBase = slotIndex * MaxGridSize;
            for (int r = 0; r < gridSize; r++)
            {
                _rowSums[vBase + r] = 0;
                _rowCounts[vBase + r] = 0;
            }
            for (int c = 0; c < gridSize; c++)
            {
                _colSums[vBase + c] = 0;
                _colCounts[vBase + c] = 0;
            }
            _diagSums[slotIndex] = 0;
            _antiDiagSums[slotIndex] = 0;
            _diagCounts[slotIndex] = 0;
            _antiDiagCounts[slotIndex] = 0;
        }

        public void ProcessPacket(int slotIndex, MovePacket packet)
        {
            int gridSize = _gridSizes[slotIndex];
            int linearIdx = packet.Row * gridSize + packet.Col;
            ulong blankBit = linearIdx < 64
                ? (_blankCellMaskLow[slotIndex] >> linearIdx) & 1UL
                : (_blankCellMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
            if (blankBit == 0)
                return;
            ulong arrivedBit = linearIdx < 64
                ? (_arrivedMaskLow[slotIndex] >> linearIdx) & 1UL
                : (_arrivedMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
            if (arrivedBit != 0)
                return;
            if (linearIdx < 64)
                _arrivedMaskLow[slotIndex] |= (1UL << linearIdx);
            else
                _arrivedMaskHigh[slotIndex] |= (1UL << (linearIdx - 64));
            int vBase = slotIndex * MaxGridSize;
            _rowSums[vBase + packet.Row] += packet.Value;
            _rowCounts[vBase + packet.Row]++;
            _colSums[vBase + packet.Col] += packet.Value;
            _colCounts[vBase + packet.Col]++;
            if (packet.Row == packet.Col)
            {
                _diagSums[slotIndex] += packet.Value;
                _diagCounts[slotIndex]++;
            }
            if (packet.Row + packet.Col == gridSize - 1)
            {
                _antiDiagSums[slotIndex] += packet.Value;
                _antiDiagCounts[slotIndex]++;
            }
        }

        public int TryRecoverSession(int slotIndex, Span<MovePacket> outputBuffer)
        {
            int gridSize = _gridSizes[slotIndex];
            uint expectedSum = _expectedSums[slotIndex];
            int vBase = slotIndex * MaxGridSize;
            uint sessionId = _sessionIdMap[slotIndex];
            int totalRecovered = 0;
            bool found;
            do
            {
                found = false;
                for (int linearIdx = 0; linearIdx < gridSize * gridSize; linearIdx++)
                {
                    ulong blankBit = linearIdx < 64
                        ? (_blankCellMaskLow[slotIndex] >> linearIdx) & 1UL
                        : (_blankCellMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
                    if (blankBit == 0)
                        continue;
                    ulong arrivedBit = linearIdx < 64
                        ? (_arrivedMaskLow[slotIndex] >> linearIdx) & 1UL
                        : (_arrivedMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
                    if (arrivedBit != 0)
                        continue;
                    int row = linearIdx / gridSize;
                    int col = linearIdx % gridSize;
                    uint candidate = 0;
                    bool hasCandidate = false;

                    int rowCount = _rowCounts[vBase + row];
                    if (rowCount == gridSize - 1)
                    {
                        candidate = expectedSum - _rowSums[vBase + row];
                        hasCandidate = true;
                    }

                    int colCount = _colCounts[vBase + col];
                    if (colCount == gridSize - 1)
                    {
                        uint cc = expectedSum - _colSums[vBase + col];
                        if (!hasCandidate)
                        {
                            candidate = cc;
                            hasCandidate = true;
                        }
                        else if (candidate != cc)
                        {
                            continue;
                        }
                    }

                    if (row == col)
                    {
                        if (_diagCounts[slotIndex] == gridSize - 1)
                        {
                            uint dc = expectedSum - _diagSums[slotIndex];
                            if (!hasCandidate)
                            {
                                candidate = dc;
                                hasCandidate = true;
                            }
                            else if (candidate != dc)
                            {
                                continue;
                            }
                        }
                    }

                    if (row + col == gridSize - 1)
                    {
                        if (_antiDiagCounts[slotIndex] == gridSize - 1)
                        {
                            uint ac = expectedSum - _antiDiagSums[slotIndex];
                            if (!hasCandidate)
                            {
                                candidate = ac;
                                hasCandidate = true;
                            }
                            else if (candidate != ac)
                            {
                                continue;
                            }
                        }
                    }

                    if (hasCandidate && candidate > 0 && candidate <= 255)
                    {
                        if (linearIdx < 64)
                            _arrivedMaskLow[slotIndex] |= (1UL << linearIdx);
                        else
                            _arrivedMaskHigh[slotIndex] |= (1UL << (linearIdx - 64));
                        _rowSums[vBase + row] += candidate;
                        _rowCounts[vBase + row]++;
                        _colSums[vBase + col] += candidate;
                        _colCounts[vBase + col]++;
                        if (row == col)
                        {
                            _diagSums[slotIndex] += candidate;
                            _diagCounts[slotIndex]++;
                        }
                        if (row + col == gridSize - 1)
                        {
                            _antiDiagSums[slotIndex] += candidate;
                            _antiDiagCounts[slotIndex]++;
                        }

                        outputBuffer[totalRecovered++] = new MovePacket
                        {
                            SessionId = sessionId,
                            Row = (byte)row,
                            Col = (byte)col,
                            Value = (byte)candidate
                        };
                        found = true;
                        if (totalRecovered >= outputBuffer.Length)
                            break;
                    }
                }
            }
            while (found && totalRecovered < outputBuffer.Length);
            return totalRecovered;
        }
    }

    public sealed class MagicSquareRecovery : IRecoveryStrategy
    {
        public const int MaxSessions = 16384;
        public const int MaxGridSize = 10;

        private readonly uint[] _expectedSums;
        private readonly int[] _gridSizes;
        private readonly uint[] _sessionIdMap;
        private readonly ulong[] _blankCellMaskLow;
        private readonly ulong[] _blankCellMaskHigh;
        private readonly ulong[] _arrivedMaskLow;
        private readonly ulong[] _arrivedMaskHigh;
        private readonly uint[] _rowSums;
        private readonly uint[] _colSums;
        private readonly uint[] _diagSums;
        private readonly uint[] _antiDiagSums;
        private readonly byte[] _rowCounts;
        private readonly byte[] _colCounts;
        private readonly byte[] _diagCounts;
        private readonly byte[] _antiDiagCounts;
        private readonly ulong[] _rowValueMaskLow;
        private readonly ulong[] _rowValueMaskHigh;
        private readonly ulong[] _colValueMaskLow;
        private readonly ulong[] _colValueMaskHigh;
        private readonly ulong[] _globalValueMaskLow;
        private readonly ulong[] _globalValueMaskHigh;

        public MagicSquareRecovery()
        {
            int s = MaxSessions;
            int v = MaxSessions * MaxGridSize;
            _expectedSums = new uint[s];
            _gridSizes = new int[s];
            _sessionIdMap = new uint[s];
            _blankCellMaskLow = new ulong[s];
            _blankCellMaskHigh = new ulong[s];
            _arrivedMaskLow = new ulong[s];
            _arrivedMaskHigh = new ulong[s];
            _rowSums = new uint[v];
            _colSums = new uint[v];
            _diagSums = new uint[s];
            _antiDiagSums = new uint[s];
            _rowCounts = new byte[v];
            _colCounts = new byte[v];
            _diagCounts = new byte[s];
            _antiDiagCounts = new byte[s];
            _rowValueMaskLow = new ulong[v];
            _rowValueMaskHigh = new ulong[v];
            _colValueMaskLow = new ulong[v];
            _colValueMaskHigh = new ulong[v];
            _globalValueMaskLow = new ulong[s];
            _globalValueMaskHigh = new ulong[s];
        }

        public void RegisterSession(int slotIndex, uint sessionId, int gridSize, uint expectedSum, ReadOnlySpan<byte> currentGrid, ReadOnlySpan<byte> solutionGrid)
        {
            _sessionIdMap[slotIndex] = sessionId;
            _expectedSums[slotIndex] = expectedSum;
            _gridSizes[slotIndex] = gridSize;
            _blankCellMaskLow[slotIndex] = 0;
            _blankCellMaskHigh[slotIndex] = 0;
            _arrivedMaskLow[slotIndex] = 0;
            _arrivedMaskHigh[slotIndex] = 0;
            int cells = gridSize * gridSize;
            for (int i = 0; i < cells; i++)
            {
                if (currentGrid[i] == 0)
                {
                    if (i < 64)
                        _blankCellMaskLow[slotIndex] |= (1UL << i);
                    else
                        _blankCellMaskHigh[slotIndex] |= (1UL << (i - 64));
                }
            }
            int vBase = slotIndex * MaxGridSize;
            for (int r = 0; r < gridSize; r++)
            {
                _rowSums[vBase + r] = 0;
                _rowCounts[vBase + r] = 0;
                _rowValueMaskLow[vBase + r] = 0;
                _rowValueMaskHigh[vBase + r] = 0;
            }
            for (int c = 0; c < gridSize; c++)
            {
                _colSums[vBase + c] = 0;
                _colCounts[vBase + c] = 0;
                _colValueMaskLow[vBase + c] = 0;
                _colValueMaskHigh[vBase + c] = 0;
            }
            _diagSums[slotIndex] = 0;
            _antiDiagSums[slotIndex] = 0;
            _diagCounts[slotIndex] = 0;
            _antiDiagCounts[slotIndex] = 0;
            _globalValueMaskLow[slotIndex] = 0;
            _globalValueMaskHigh[slotIndex] = 0;

            for (int i = 0; i < cells; i++)
            {
                byte val = currentGrid[i];
                if (val != 0)
                {
                    int r = i / gridSize;
                    int c = i % gridSize;
                    int bit = val - 1;
                    if (bit < 64)
                    {
                        _rowValueMaskLow[vBase + r] |= (1UL << bit);
                        _colValueMaskLow[vBase + c] |= (1UL << bit);
                        _globalValueMaskLow[slotIndex] |= (1UL << bit);
                    }
                    else
                    {
                        _rowValueMaskHigh[vBase + r] |= (1UL << (bit - 64));
                        _colValueMaskHigh[vBase + c] |= (1UL << (bit - 64));
                        _globalValueMaskHigh[slotIndex] |= (1UL << (bit - 64));
                    }
                }
            }
        }

        public void ProcessPacket(int slotIndex, MovePacket packet)
        {
            int gridSize = _gridSizes[slotIndex];
            int linearIdx = packet.Row * gridSize + packet.Col;
            ulong blankBit = linearIdx < 64
                ? (_blankCellMaskLow[slotIndex] >> linearIdx) & 1UL
                : (_blankCellMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
            if (blankBit == 0)
                return;
            ulong arrivedBit = linearIdx < 64
                ? (_arrivedMaskLow[slotIndex] >> linearIdx) & 1UL
                : (_arrivedMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
            if (arrivedBit != 0)
                return;
            if (linearIdx < 64)
                _arrivedMaskLow[slotIndex] |= (1UL << linearIdx);
            else
                _arrivedMaskHigh[slotIndex] |= (1UL << (linearIdx - 64));
            int vBase = slotIndex * MaxGridSize;
            _rowSums[vBase + packet.Row] += packet.Value;
            _rowCounts[vBase + packet.Row]++;
            _colSums[vBase + packet.Col] += packet.Value;
            _colCounts[vBase + packet.Col]++;
            if (packet.Row == packet.Col)
            {
                _diagSums[slotIndex] += packet.Value;
                _diagCounts[slotIndex]++;
            }
            if (packet.Row + packet.Col == gridSize - 1)
            {
                _antiDiagSums[slotIndex] += packet.Value;
                _antiDiagCounts[slotIndex]++;
            }

            int bit = packet.Value - 1;
            if (bit < 64)
            {
                _rowValueMaskLow[vBase + packet.Row] |= (1UL << bit);
                _colValueMaskLow[vBase + packet.Col] |= (1UL << bit);
                _globalValueMaskLow[slotIndex] |= (1UL << bit);
            }
            else
            {
                _rowValueMaskHigh[vBase + packet.Row] |= (1UL << (bit - 64));
                _colValueMaskHigh[vBase + packet.Col] |= (1UL << (bit - 64));
                _globalValueMaskHigh[slotIndex] |= (1UL << (bit - 64));
            }
        }

        public int TryRecoverSession(int slotIndex, Span<MovePacket> outputBuffer)
        {
            int gridSize = _gridSizes[slotIndex];
            uint expectedSum = _expectedSums[slotIndex];
            int vBase = slotIndex * MaxGridSize;
            uint sessionId = _sessionIdMap[slotIndex];
            int totalRecovered = 0;
            bool found;
            do
            {
                found = false;
                for (int linearIdx = 0; linearIdx < gridSize * gridSize; linearIdx++)
                {
                    ulong blankBit = linearIdx < 64
                        ? (_blankCellMaskLow[slotIndex] >> linearIdx) & 1UL
                        : (_blankCellMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
                    if (blankBit == 0)
                        continue;
                    ulong arrivedBit = linearIdx < 64
                        ? (_arrivedMaskLow[slotIndex] >> linearIdx) & 1UL
                        : (_arrivedMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
                    if (arrivedBit != 0)
                        continue;
                    int row = linearIdx / gridSize;
                    int col = linearIdx % gridSize;
                    uint candidate = 0;
                    bool hasCandidate = false;

                    int rowCount = _rowCounts[vBase + row];
                    if (rowCount == gridSize - 1)
                    {
                        candidate = expectedSum - _rowSums[vBase + row];
                        hasCandidate = true;
                    }

                    int colCount = _colCounts[vBase + col];
                    if (colCount == gridSize - 1)
                    {
                        uint cc = expectedSum - _colSums[vBase + col];
                        if (!hasCandidate)
                        {
                            candidate = cc;
                            hasCandidate = true;
                        }
                        else if (candidate != cc)
                        {
                            continue;
                        }
                    }

                    if (row == col)
                    {
                        if (_diagCounts[slotIndex] == gridSize - 1)
                        {
                            uint dc = expectedSum - _diagSums[slotIndex];
                            if (!hasCandidate)
                            {
                                candidate = dc;
                                hasCandidate = true;
                            }
                            else if (candidate != dc)
                            {
                                continue;
                            }
                        }
                    }

                    if (row + col == gridSize - 1)
                    {
                        if (_antiDiagCounts[slotIndex] == gridSize - 1)
                        {
                            uint ac = expectedSum - _antiDiagSums[slotIndex];
                            if (!hasCandidate)
                            {
                                candidate = ac;
                                hasCandidate = true;
                            }
                            else if (candidate != ac)
                            {
                                continue;
                            }
                        }
                    }

                    if (hasCandidate && candidate > 0 && candidate <= 255)
                    {
                        int val = (byte)candidate;
                        int bit = val - 1;

                        bool rowHasValue = bit < 64
                            ? ((_rowValueMaskLow[vBase + row] >> bit) & 1UL) != 0
                            : ((_rowValueMaskHigh[vBase + row] >> (bit - 64)) & 1UL) != 0;
                        if (rowHasValue) continue;

                        bool colHasValue = bit < 64
                            ? ((_colValueMaskLow[vBase + col] >> bit) & 1UL) != 0
                            : ((_colValueMaskHigh[vBase + col] >> (bit - 64)) & 1UL) != 0;
                        if (colHasValue) continue;

                        bool globalHasValue = bit < 64
                            ? ((_globalValueMaskLow[slotIndex] >> bit) & 1UL) != 0
                            : ((_globalValueMaskHigh[slotIndex] >> (bit - 64)) & 1UL) != 0;
                        if (globalHasValue) continue;

                        if (linearIdx < 64)
                            _arrivedMaskLow[slotIndex] |= (1UL << linearIdx);
                        else
                            _arrivedMaskHigh[slotIndex] |= (1UL << (linearIdx - 64));
                        _rowSums[vBase + row] += candidate;
                        _rowCounts[vBase + row]++;
                        _colSums[vBase + col] += candidate;
                        _colCounts[vBase + col]++;
                        if (row == col)
                        {
                            _diagSums[slotIndex] += candidate;
                            _diagCounts[slotIndex]++;
                        }
                        if (row + col == gridSize - 1)
                        {
                            _antiDiagSums[slotIndex] += candidate;
                            _antiDiagCounts[slotIndex]++;
                        }

                        if (bit < 64)
                        {
                            _rowValueMaskLow[vBase + row] |= (1UL << bit);
                            _colValueMaskLow[vBase + col] |= (1UL << bit);
                            _globalValueMaskLow[slotIndex] |= (1UL << bit);
                        }
                        else
                        {
                            _rowValueMaskHigh[vBase + row] |= (1UL << (bit - 64));
                            _colValueMaskHigh[vBase + col] |= (1UL << (bit - 64));
                            _globalValueMaskHigh[slotIndex] |= (1UL << (bit - 64));
                        }

                        outputBuffer[totalRecovered++] = new MovePacket
                        {
                            SessionId = sessionId,
                            Row = (byte)row,
                            Col = (byte)col,
                            Value = (byte)candidate
                        };
                        found = true;
                        if (totalRecovered >= outputBuffer.Length)
                            break;
                    }
                }
            }
            while (found && totalRecovered < outputBuffer.Length);
            return totalRecovered;
        }
    }

    public sealed class HexagonalLatticeRecovery : IRecoveryStrategy
    {
        public const int MaxSessions = 16384;
        public const int MaxGridSize = 10;
        public const int MaxGridCells = 100;

        private readonly uint[] _expectedSums;
        private readonly int[] _gridSizes;
        private readonly uint[] _sessionIdMap;
        private readonly ulong[] _blankCellMaskLow;
        private readonly ulong[] _blankCellMaskHigh;
        private readonly ulong[] _arrivedMaskLow;
        private readonly ulong[] _arrivedMaskHigh;
        private readonly uint[] _rowSums;
        private readonly uint[] _colSums;
        private readonly uint[] _diagSums;
        private readonly uint[] _antiDiagSums;
        private readonly byte[] _rowCounts;
        private readonly byte[] _colCounts;
        private readonly byte[] _diagCounts;
        private readonly byte[] _antiDiagCounts;

        private readonly uint[] _hexExpectedSums;
        private readonly uint[] _hexCurrentSums;
        private readonly byte[] _hexCounts;
        private readonly byte[] _hexGroupSizes;
        private readonly byte[] _hexMemberToGroups;
        private readonly byte[] _hexMemberToGroupCount;

        public HexagonalLatticeRecovery()
        {
            int s = MaxSessions;
            int v = MaxSessions * MaxGridSize;
            int c = MaxSessions * MaxGridCells;
            int mg = MaxSessions * MaxGridCells * 7;

            _expectedSums = new uint[s];
            _gridSizes = new int[s];
            _sessionIdMap = new uint[s];
            _blankCellMaskLow = new ulong[s];
            _blankCellMaskHigh = new ulong[s];
            _arrivedMaskLow = new ulong[s];
            _arrivedMaskHigh = new ulong[s];
            _rowSums = new uint[v];
            _colSums = new uint[v];
            _diagSums = new uint[s];
            _antiDiagSums = new uint[s];
            _rowCounts = new byte[v];
            _colCounts = new byte[v];
            _diagCounts = new byte[s];
            _antiDiagCounts = new byte[s];

            _hexExpectedSums = new uint[c];
            _hexCurrentSums = new uint[c];
            _hexCounts = new byte[c];
            _hexGroupSizes = new byte[c];
            _hexMemberToGroups = new byte[mg];
            _hexMemberToGroupCount = new byte[c];
        }

        public void RegisterSession(int slotIndex, uint sessionId, int gridSize, uint expectedSum, ReadOnlySpan<byte> currentGrid, ReadOnlySpan<byte> solutionGrid)
        {
            _sessionIdMap[slotIndex] = sessionId;
            _expectedSums[slotIndex] = expectedSum;
            _gridSizes[slotIndex] = gridSize;
            _blankCellMaskLow[slotIndex] = 0;
            _blankCellMaskHigh[slotIndex] = 0;
            _arrivedMaskLow[slotIndex] = 0;
            _arrivedMaskHigh[slotIndex] = 0;
            int cells = gridSize * gridSize;
            for (int i = 0; i < cells; i++)
            {
                if (currentGrid[i] == 0)
                {
                    if (i < 64)
                        _blankCellMaskLow[slotIndex] |= (1UL << i);
                    else
                        _blankCellMaskHigh[slotIndex] |= (1UL << (i - 64));
                }
            }
            int vBase = slotIndex * MaxGridSize;
            for (int r = 0; r < gridSize; r++)
            {
                _rowSums[vBase + r] = 0;
                _rowCounts[vBase + r] = 0;
            }
            for (int c = 0; c < gridSize; c++)
            {
                _colSums[vBase + c] = 0;
                _colCounts[vBase + c] = 0;
            }
            _diagSums[slotIndex] = 0;
            _antiDiagSums[slotIndex] = 0;
            _diagCounts[slotIndex] = 0;
            _antiDiagCounts[slotIndex] = 0;

            int hBase = slotIndex * MaxGridCells;
            int mBase = slotIndex * MaxGridCells * 7;
            for (int i = 0; i < MaxGridCells; i++)
            {
                _hexExpectedSums[hBase + i] = 0;
                _hexCurrentSums[hBase + i] = 0;
                _hexCounts[hBase + i] = 0;
                _hexGroupSizes[hBase + i] = 0;
                _hexMemberToGroupCount[hBase + i] = 0;
                for (int g = 0; g < 7; g++)
                {
                    _hexMemberToGroups[mBase + i * 7 + g] = 0;
                }
            }

            for (int r = 1; r < gridSize - 1; r++)
            {
                for (int c = 1; c < gridSize - 1; c++)
                {
                    int centerIdx = r * gridSize + c;
                    _hexGroupSizes[hBase + centerIdx] = 7;

                    uint groupSum = 0;

                    // center (r, c)
                    groupSum += solutionGrid[centerIdx];
                    int cnt = _hexMemberToGroupCount[hBase + centerIdx];
                    _hexMemberToGroups[mBase + centerIdx * 7 + cnt] = (byte)centerIdx;
                    _hexMemberToGroupCount[hBase + centerIdx] = (byte)(cnt + 1);

                    // top-left (r-1, c-1)
                    int tl = (r - 1) * gridSize + (c - 1);
                    groupSum += solutionGrid[tl];
                    cnt = _hexMemberToGroupCount[hBase + tl];
                    _hexMemberToGroups[mBase + tl * 7 + cnt] = (byte)centerIdx;
                    _hexMemberToGroupCount[hBase + tl] = (byte)(cnt + 1);

                    // top (r-1, c)
                    int t = (r - 1) * gridSize + c;
                    groupSum += solutionGrid[t];
                    cnt = _hexMemberToGroupCount[hBase + t];
                    _hexMemberToGroups[mBase + t * 7 + cnt] = (byte)centerIdx;
                    _hexMemberToGroupCount[hBase + t] = (byte)(cnt + 1);

                    // left (r, c-1)
                    int l = r * gridSize + (c - 1);
                    groupSum += solutionGrid[l];
                    cnt = _hexMemberToGroupCount[hBase + l];
                    _hexMemberToGroups[mBase + l * 7 + cnt] = (byte)centerIdx;
                    _hexMemberToGroupCount[hBase + l] = (byte)(cnt + 1);

                    // right (r, c+1)
                    int rr = r * gridSize + (c + 1);
                    groupSum += solutionGrid[rr];
                    cnt = _hexMemberToGroupCount[hBase + rr];
                    _hexMemberToGroups[mBase + rr * 7 + cnt] = (byte)centerIdx;
                    _hexMemberToGroupCount[hBase + rr] = (byte)(cnt + 1);

                    // bottom (r+1, c)
                    int b = (r + 1) * gridSize + c;
                    groupSum += solutionGrid[b];
                    cnt = _hexMemberToGroupCount[hBase + b];
                    _hexMemberToGroups[mBase + b * 7 + cnt] = (byte)centerIdx;
                    _hexMemberToGroupCount[hBase + b] = (byte)(cnt + 1);

                    // bottom-right (r+1, c+1)
                    int br = (r + 1) * gridSize + (c + 1);
                    groupSum += solutionGrid[br];
                    cnt = _hexMemberToGroupCount[hBase + br];
                    _hexMemberToGroups[mBase + br * 7 + cnt] = (byte)centerIdx;
                    _hexMemberToGroupCount[hBase + br] = (byte)(cnt + 1);

                    _hexExpectedSums[hBase + centerIdx] = groupSum;
                }
            }
        }

        public void ProcessPacket(int slotIndex, MovePacket packet)
        {
            int gridSize = _gridSizes[slotIndex];
            int linearIdx = packet.Row * gridSize + packet.Col;
            ulong blankBit = linearIdx < 64
                ? (_blankCellMaskLow[slotIndex] >> linearIdx) & 1UL
                : (_blankCellMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
            if (blankBit == 0)
                return;
            ulong arrivedBit = linearIdx < 64
                ? (_arrivedMaskLow[slotIndex] >> linearIdx) & 1UL
                : (_arrivedMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
            if (arrivedBit != 0)
                return;
            if (linearIdx < 64)
                _arrivedMaskLow[slotIndex] |= (1UL << linearIdx);
            else
                _arrivedMaskHigh[slotIndex] |= (1UL << (linearIdx - 64));

            int vBase = slotIndex * MaxGridSize;
            _rowSums[vBase + packet.Row] += packet.Value;
            _rowCounts[vBase + packet.Row]++;
            _colSums[vBase + packet.Col] += packet.Value;
            _colCounts[vBase + packet.Col]++;
            if (packet.Row == packet.Col)
            {
                _diagSums[slotIndex] += packet.Value;
                _diagCounts[slotIndex]++;
            }
            if (packet.Row + packet.Col == gridSize - 1)
            {
                _antiDiagSums[slotIndex] += packet.Value;
                _antiDiagCounts[slotIndex]++;
            }

            int hBase = slotIndex * MaxGridCells;
            int mBase = slotIndex * MaxGridCells * 7;
            int groupCount = _hexMemberToGroupCount[hBase + linearIdx];
            for (int g = 0; g < groupCount; g++)
            {
                int groupIdx = _hexMemberToGroups[mBase + linearIdx * 7 + g];
                _hexCurrentSums[hBase + groupIdx] += packet.Value;
                _hexCounts[hBase + groupIdx]++;
            }
        }

        public int TryRecoverSession(int slotIndex, Span<MovePacket> outputBuffer)
        {
            int gridSize = _gridSizes[slotIndex];
            uint expectedSum = _expectedSums[slotIndex];
            int vBase = slotIndex * MaxGridSize;
            uint sessionId = _sessionIdMap[slotIndex];
            int hBase = slotIndex * MaxGridCells;
            int mBase = slotIndex * MaxGridCells * 7;
            int totalRecovered = 0;
            bool found;
            do
            {
                found = false;
                for (int linearIdx = 0; linearIdx < gridSize * gridSize; linearIdx++)
                {
                    ulong blankBit = linearIdx < 64
                        ? (_blankCellMaskLow[slotIndex] >> linearIdx) & 1UL
                        : (_blankCellMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
                    if (blankBit == 0)
                        continue;
                    ulong arrivedBit = linearIdx < 64
                        ? (_arrivedMaskLow[slotIndex] >> linearIdx) & 1UL
                        : (_arrivedMaskHigh[slotIndex] >> (linearIdx - 64)) & 1UL;
                    if (arrivedBit != 0)
                        continue;

                    int row = linearIdx / gridSize;
                    int col = linearIdx % gridSize;
                    uint candidate = 0;
                    bool hasCandidate = false;

                    int groupCount = _hexMemberToGroupCount[hBase + linearIdx];
                    for (int g = 0; g < groupCount; g++)
                    {
                        int groupIdx = _hexMemberToGroups[mBase + linearIdx * 7 + g];
                        int groupSize = _hexGroupSizes[hBase + groupIdx];
                        if (groupSize == 0)
                            continue;
                        if (_hexCounts[hBase + groupIdx] == groupSize - 1)
                        {
                            uint hc = _hexExpectedSums[hBase + groupIdx] - _hexCurrentSums[hBase + groupIdx];
                            if (!hasCandidate)
                            {
                                candidate = hc;
                                hasCandidate = true;
                            }
                            else if (candidate != hc)
                            {
                                hasCandidate = false;
                                break;
                            }
                        }
                    }

                    if (!hasCandidate)
                    {
                        int rowCount = _rowCounts[vBase + row];
                        if (rowCount == gridSize - 1)
                        {
                            candidate = expectedSum - _rowSums[vBase + row];
                            hasCandidate = true;
                        }

                        int colCount = _colCounts[vBase + col];
                        if (colCount == gridSize - 1)
                        {
                            uint cc = expectedSum - _colSums[vBase + col];
                            if (!hasCandidate)
                            {
                                candidate = cc;
                                hasCandidate = true;
                            }
                            else if (candidate != cc)
                            {
                                continue;
                            }
                        }

                        if (row == col)
                        {
                            if (_diagCounts[slotIndex] == gridSize - 1)
                            {
                                uint dc = expectedSum - _diagSums[slotIndex];
                                if (!hasCandidate)
                                {
                                    candidate = dc;
                                    hasCandidate = true;
                                }
                                else if (candidate != dc)
                                {
                                    continue;
                                }
                            }
                        }

                        if (row + col == gridSize - 1)
                        {
                            if (_antiDiagCounts[slotIndex] == gridSize - 1)
                            {
                                uint ac = expectedSum - _antiDiagSums[slotIndex];
                                if (!hasCandidate)
                                {
                                    candidate = ac;
                                    hasCandidate = true;
                                }
                                else if (candidate != ac)
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    if (hasCandidate && candidate > 0 && candidate <= 255)
                    {
                        if (linearIdx < 64)
                            _arrivedMaskLow[slotIndex] |= (1UL << linearIdx);
                        else
                            _arrivedMaskHigh[slotIndex] |= (1UL << (linearIdx - 64));

                        _rowSums[vBase + row] += candidate;
                        _rowCounts[vBase + row]++;
                        _colSums[vBase + col] += candidate;
                        _colCounts[vBase + col]++;
                        if (row == col)
                        {
                            _diagSums[slotIndex] += candidate;
                            _diagCounts[slotIndex]++;
                        }
                        if (row + col == gridSize - 1)
                        {
                            _antiDiagSums[slotIndex] += candidate;
                            _antiDiagCounts[slotIndex]++;
                        }

                        for (int g = 0; g < groupCount; g++)
                        {
                            int groupIdx = _hexMemberToGroups[mBase + linearIdx * 7 + g];
                            _hexCurrentSums[hBase + groupIdx] += candidate;
                            _hexCounts[hBase + groupIdx]++;
                        }

                        outputBuffer[totalRecovered++] = new MovePacket
                        {
                            SessionId = sessionId,
                            Row = (byte)row,
                            Col = (byte)col,
                            Value = (byte)candidate
                        };
                        found = true;
                        if (totalRecovered >= outputBuffer.Length)
                            break;
                    }
                }
            }
            while (found && totalRecovered < outputBuffer.Length);
            return totalRecovered;
        }
    }

    public sealed class GameSessionManager
    {
        public const int MaxSessions = 16384;
        public const int MaxGridCells = 100;

        private readonly byte[] _currentGrids;
        private readonly byte[] _solutionGrids;
        internal readonly int[] _gridSizes;
        internal readonly uint[] _expectedSums;
        private readonly int[] _remainingEmpty;
        internal readonly int[] _sessionStates;
        private readonly uint[] _sessionIds;
        internal readonly int[] _slotGenerations;

        public GameSessionManager()
        {
            _currentGrids = new byte[MaxSessions * MaxGridCells];
            _solutionGrids = new byte[MaxSessions * MaxGridCells];
            _gridSizes = new int[MaxSessions];
            _expectedSums = new uint[MaxSessions];
            _remainingEmpty = new int[MaxSessions];
            _sessionStates = new int[MaxSessions];
            _sessionIds = new uint[MaxSessions];
            _slotGenerations = new int[MaxSessions];
        }

        public bool TryCreateSession(out int slotIndex, out uint sessionId)
        {
            for (int i = 0; i < MaxSessions; i++)
            {
                if (Interlocked.CompareExchange(ref _sessionStates[i], 1, 0) == 0)
                {
                    slotIndex = i;
                    _slotGenerations[i]++;
                    int gen = _slotGenerations[i] & 0xFFFF;
                    sessionId = (uint)(((uint)gen << 16) | (uint)i);
                    _sessionIds[i] = sessionId;
                    var random = new Random((int)(sessionId ^ Environment.TickCount));
                    int gridSize = random.Next(3, 11);
                    _gridSizes[i] = gridSize;
                    int cells = gridSize * gridSize;
                    var solSpan = GetSolutionSpan(i);
                    OrthogonalLatinSquareGenerator.Generate(gridSize, solSpan);
                    solSpan.Slice(0, cells).CopyTo(GetCurrentSpan(i));
                    uint magicSum = OrthogonalLatinSquareGenerator.ComputeMagicSum(gridSize);
                    _expectedSums[i] = magicSum;
                    int targetEmpty = cells / 3 + random.Next(cells / 3);
                    int emptyCount = 0;
                    var curSpan = GetCurrentSpan(i);
                    for (int e = 0; e < targetEmpty; e++)
                    {
                        int idx = random.Next(cells);
                        if (curSpan[idx] != 0)
                        {
                            curSpan[idx] = 0;
                            emptyCount++;
                        }
                    }
                    _remainingEmpty[i] = emptyCount;
                    return true;
                }
            }
            slotIndex = -1;
            sessionId = 0;
            return false;
        }

        public void ResetCompletedSession(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaxSessions)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref _sessionStates[slotIndex], 0, 2) == 2)
            {
            }
        }

        public void ApplyMove(int slotIndex, MovePacket packet)
        {
            if (slotIndex < 0 || slotIndex >= MaxSessions)
            {
                return;
            }
            if (_sessionStates[slotIndex] != 1)
            {
                return;
            }
            int gridSize = _gridSizes[slotIndex];
            if (packet.Row >= gridSize || packet.Col >= gridSize)
            {
                return;
            }
            int idx = packet.Row * gridSize + packet.Col;
            var curSpan = GetCurrentSpan(slotIndex);
            if (curSpan[idx] != 0)
            {
                return;
            }
            var solSpan = GetSolutionSpan(slotIndex);
            if (packet.Value == solSpan[idx])
            {
                curSpan[idx] = packet.Value;
                int remaining = Interlocked.Decrement(ref _remainingEmpty[slotIndex]);
                if (remaining == 0)
                {
                    Interlocked.Exchange(ref _sessionStates[slotIndex], 2);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetCurrentSpan(int slotIndex)
        {
            return new Span<byte>(_currentGrids, slotIndex * MaxGridCells, MaxGridCells);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSolutionSpan(int slotIndex)
        {
            return new Span<byte>(_solutionGrids, slotIndex * MaxGridCells, MaxGridCells);
        }

        public bool TryGetRandomActiveSession(Random random, out int slotIndex, out uint sessionId, out int gridSize)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int idx = random.Next(MaxSessions);
                if (_sessionStates[idx] == 1)
                {
                    slotIndex = idx;
                    sessionId = _sessionIds[idx];
                    gridSize = _gridSizes[idx];
                    return true;
                }
            }
            slotIndex = -1;
            sessionId = 0;
            gridSize = 0;
            return false;
        }

        public bool TryGetRandomBlankCell(Random random, int slotIndex, out int row, out int col, out byte solutionValue)
        {
            int gridSize = _gridSizes[slotIndex];
            int cells = gridSize * gridSize;
            int offset = slotIndex * MaxGridCells;
            int blankCount = 0;
            for (int i = 0; i < cells; i++)
            {
                if (_currentGrids[offset + i] == 0) blankCount++;
            }
            if (blankCount == 0)
            {
                row = 0; col = 0; solutionValue = 0;
                return false;
            }
            int target = random.Next(blankCount);
            int found = 0;
            for (int i = 0; i < cells; i++)
            {
                if (_currentGrids[offset + i] == 0)
                {
                    if (found == target)
                    {
                        row = i / gridSize;
                        col = i % gridSize;
                        solutionValue = _solutionGrids[offset + i];
                        return true;
                    }
                    found++;
                }
            }
            row = 0; col = 0; solutionValue = 0;
            return false;
        }

        public int CountActiveSessions()
        {
            int count = 0;
            for (int i = 0; i < MaxSessions; i++)
            {
                if (_sessionStates[i] == 1)
                {
                    count++;
                }
            }
            return count;
        }

        public int CountWonSessions()
        {
            int count = 0;
            for (int i = 0; i < MaxSessions; i++)
            {
                if (_sessionStates[i] == 2)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public static class OrthogonalLatinSquareGenerator
    {
        public static void Generate(int gridSize, Span<byte> target)
        {
            if (gridSize % 2 == 1)
            {
                GenerateOdd(gridSize, target);
            }
            else if (gridSize % 4 == 0)
            {
                GenerateDoublyEven(gridSize, target);
            }
            else
            {
                GenerateSinglyEven(gridSize, target);
            }
        }

        private static void GenerateOdd(int n, Span<byte> target)
        {
            int total = n * n;
            for (int i = 0; i < total; i++)
            {
                target[i] = 0;
            }
            int row = 0;
            int col = n / 2;
            for (int val = 1; val <= total; val++)
            {
                target[row * n + col] = (byte)val;
                int nextRow = (row - 1 + n) % n;
                int nextCol = (col + 1) % n;
                if (target[nextRow * n + nextCol] != 0)
                {
                    row = (row + 1) % n;
                }
                else
                {
                    row = nextRow;
                    col = nextCol;
                }
            }
        }

        private static void GenerateDoublyEven(int n, Span<byte> target)
        {
            int total = n * n;
            for (int i = 0; i < total; i++)
            {
                target[i] = (byte)(i + 1);
            }
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    if ((r % 4 == c % 4) || (r % 4 + c % 4 == 3))
                    {
                        int idx = r * n + c;
                        target[idx] = (byte)(total + 1 - target[idx]);
                    }
                }
            }
        }

        private static void GenerateSinglyEven(int n, Span<byte> target)
        {
            int halfN = n / 2;
            int subSquareSize = halfN * halfN;
            Span<byte> sub = stackalloc byte[subSquareSize];
            GenerateOdd(halfN, sub);
            ReadOnlySpan<int> quadrantFactors = stackalloc int[4] { 0, 2, 3, 1 };
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int quadrant = (r / halfN) * 2 + (c / halfN);
                    int baseVal = sub[(r % halfN) * halfN + (c % halfN)];
                    target[r * n + c] = (byte)(baseVal + quadrantFactors[quadrant] * subSquareSize);
                }
            }
            int nColsLeft = halfN / 2;
            int nColsRight = nColsLeft - 1;
            for (int r = 0; r < halfN; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    bool shouldSwap = (c < nColsLeft) || (c >= n - nColsRight) || (c == nColsLeft && r == nColsLeft);
                    bool exclude = (c == 0 && r == nColsLeft);
                    if (shouldSwap && !exclude)
                    {
                        int idx1 = r * n + c;
                        int idx2 = (r + halfN) * n + c;
                        byte tmp = target[idx1];
                        target[idx1] = target[idx2];
                        target[idx2] = tmp;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeMagicSum(int gridSize)
        {
            return (uint)(gridSize * (gridSize * gridSize + 1) / 2);
        }
    }

    public sealed class LockFreeRingBuffer
    {
        private readonly int[] _buffer;
        private readonly long[] _sequences;
        private readonly int _mask;
        private long _head;
        private long _tail;

        public LockFreeRingBuffer(int capacity)
        {
            int size = 1;
            while (size < capacity)
            {
                size <<= 1;
            }
            _buffer = new int[size];
            _sequences = new long[size];
            _mask = size - 1;
            for (int i = 0; i < size; i++)
            {
                _sequences[i] = i;
            }
            _head = 0;
            _tail = 0;
        }

        public bool TryEnqueue(int item)
        {
            while (true)
            {
                long seq = Interlocked.Read(ref _head);
                int idx = (int)(seq & _mask);
                if (Interlocked.Read(ref _sequences[idx]) != seq)
                {
                    return false;
                }
                if (Interlocked.CompareExchange(ref _head, seq + 1, seq) == seq)
                {
                    _buffer[idx] = item;
                    Interlocked.Exchange(ref _sequences[idx], seq + 1);
                    return true;
                }
            }
        }

        public bool TryDequeue(out int item)
        {
            while (true)
            {
                long seq = Interlocked.Read(ref _tail);
                int idx = (int)(seq & _mask);
                if (Interlocked.Read(ref _sequences[idx]) != seq + 1)
                {
                    item = 0;
                    return false;
                }
                if (Interlocked.CompareExchange(ref _tail, seq + 1, seq) == seq)
                {
                    item = _buffer[idx];
                    Interlocked.Exchange(ref _sequences[idx], seq + _buffer.Length);
                    return true;
                }
            }
        }
    }
}
