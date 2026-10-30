using System.Numerics;
using System.Runtime.Intrinsics;

namespace Aurelian.RouterResearch;

internal static class RouterCpu
{
    public static void Evaluate(RouterFixture fixture, uint[] output, string implementation)
    {
        if (implementation.StartsWith("parallel", StringComparison.Ordinal))
        {
            int workers = implementation == "parallel-all" ? Environment.ProcessorCount : Math.Min(Environment.ProcessorCount, 8);
            Parallel.For(0, workers, worker => EvaluateRange(fixture, output,
                fixture.Count * worker / workers, fixture.Count * (worker + 1) / workers, true, false));
            return;
        }
        EvaluateRange(fixture, output, 0, fixture.Count, implementation == "simd", implementation == "oracle");
    }

    private static void EvaluateRange(RouterFixture fixture, uint[] output, int first, int last,
        bool vectorized, bool oracle)
    {
        uint[] tables = fixture.Tables;
        for (int index = first; index < last; index++)
        {
            ReadOnlySpan<byte> frame = fixture.Packets.AsSpan(index * RouterFixture.FrameBytes, RouterFixture.FrameBytes);
            int result = index * RouterFixture.ResultWords;
            output[result] = 3;
            output[result + 1] = RouterFixture.Missing;
            output[result + 2] = 0;
            output[result + 3] = RouterFixture.Missing;
            output[result + 4] = tables[3];
            uint protocol = frame[23];
            uint length = RouterFixture.Read16(frame, 16);
            if (RouterFixture.Read16(frame, 12) != 0x0800 || frame[14] != 0x45
                || (RouterFixture.Read16(frame, 20) & 0x3FFF) != 0
                || length > 46 || length < 28 || (protocol != 6 && protocol != 17)
                || (protocol == 6 && length < 40) || RouterFixture.HeaderChecksum(frame) != 0)
            {
                continue;
            }
            uint ttl = frame[22];
            if (ttl <= 1)
            {
                output[result] = 4;
                continue;
            }
            uint source = RouterFixture.Read32(frame, 26);
            uint destination = RouterFixture.Read32(frame, 30);
            uint port = RouterFixture.Read16(frame, 36);
            int rule;
            bool deny;
            if (oracle)
            {
                rule = FindOracleRule(fixture.Rules, source, destination, port, protocol);
                deny = rule >= 0 && fixture.Rules[rule].Deny == 1;
            }
            else
            {
                int bucket = (protocol == 17 ? 16 : 0) + (int)(port / 4096);
                int header = (int)tables[6] + bucket * 3;
                int offset = (int)tables[header];
                int count = (int)tables[header + 1];
                int stride = (int)tables[header + 2];
                int lane = FindIndexedRule(tables, offset, count, stride, source, destination, port, vectorized);
                rule = lane < 0 ? -1 : (int)tables[offset + stride * 7 + lane];
                deny = lane >= 0 && tables[offset + stride * 6 + lane] == 1;
            }
            output[result + 3] = rule < 0 ? RouterFixture.Missing : (uint)rule;
            if (deny)
            {
                output[result] = 6;
                continue;
            }
            if ((destination >> 16) != 0x0A00 || ((destination >> 8) & 255) == 255)
            {
                output[result] = 5;
                continue;
            }
            int group = (int)((destination >> 8) & 255);
            uint bestPort = RouterFixture.Missing;
            uint bestCongestion = RouterFixture.Missing;
            for (int candidate = 0; oracle && candidate < fixture.Candidates; candidate++)
            {
                int entry = (int)tables[4] + (group * fixture.Candidates + candidate) * 3;
                uint candidatePort = tables[entry];
                uint congestion = tables[entry + 2];
                if (tables[entry + 1] == 1 && (congestion < bestCongestion
                    || (congestion == bestCongestion && candidatePort < bestPort)))
                {
                    bestPort = candidatePort;
                    bestCongestion = congestion;
                }
            }
            if (!oracle)
            {
                bestPort = tables[(int)tables[5] + group];
            }
            if (bestPort == RouterFixture.Missing)
            {
                output[result] = 2;
                continue;
            }
            uint oldWord = (ttl << 8) | protocol;
            uint newWord = ((ttl - 1) << 8) | protocol;
            uint checksum = (~RouterFixture.Read16(frame, 24) & 65535) + (~oldWord & 65535) + newWord;
            checksum = (checksum & 65535) + (checksum >> 16);
            checksum = (checksum & 65535) + (checksum >> 16);
            checksum = ~checksum & 65535;
            output[result] = 0;
            output[result + 1] = bestPort;
            output[result + 2] = ((ttl - 1) << 16) | checksum;
        }
    }

    private static int FindOracleRule(PolicyRule[] rules, uint source, uint destination, uint port, uint protocol)
    {
        for (int index = 0; index < rules.Length; index++)
        {
            var rule = rules[index];
            if ((rule.Protocol == 0 || protocol == rule.Protocol)
                && (source & rule.SourceMask) == rule.Source
                && (destination & rule.DestinationMask) == rule.Destination
                && port >= rule.PortLow && port <= rule.PortHigh)
            {
                return index;
            }
        }
        return -1;
    }

    private static int FindIndexedRule(uint[] tables, int offset, int count, int stride,
        uint source, uint destination, uint port, bool vectorized)
    {
        int index = 0;
        if (vectorized && Vector256.IsHardwareAccelerated)
        {
            var sources = Vector256.Create(source);
            var destinations = Vector256.Create(destination);
            var ports = Vector256.Create(port);
            for (; index < count; index += 8)
            {
                var sourceMatches = Vector256.Equals(
                    sources & Vector256.LoadUnsafe(ref tables[offset + stride + index]),
                    Vector256.LoadUnsafe(ref tables[offset + index]));
                var destinationMatches = Vector256.Equals(
                    destinations & Vector256.LoadUnsafe(ref tables[offset + stride * 3 + index]),
                    Vector256.LoadUnsafe(ref tables[offset + stride * 2 + index]));
                var lowMatches = Vector256.GreaterThanOrEqual(ports,
                    Vector256.LoadUnsafe(ref tables[offset + stride * 4 + index]));
                var highMatches = Vector256.LessThanOrEqual(ports,
                    Vector256.LoadUnsafe(ref tables[offset + stride * 5 + index]));
                uint mask = (sourceMatches & destinationMatches & lowMatches & highMatches).ExtractMostSignificantBits();
                mask &= (1u << Math.Min(8, count - index)) - 1;
                if (mask != 0)
                {
                    return index + BitOperations.TrailingZeroCount(mask);
                }
            }
            return -1;
        }
        for (; index < count; index++)
        {
            if ((source & tables[offset + stride + index]) == tables[offset + index]
                && (destination & tables[offset + stride * 3 + index]) == tables[offset + stride * 2 + index]
                && port >= tables[offset + stride * 4 + index]
                && port <= tables[offset + stride * 5 + index])
            {
                return index;
            }
        }
        return -1;
    }

    // Commit preserves input order. Generation mismatch must never actuate stale decisions.
    // Queue output contains packet indices for the host queue owner; it is not an egress scheduler.
    public static ulong Commit(RouterFixture fixture, uint[] decisions, byte[] rewrittenFrames, int[] queued)
    {
        for (int index = 0; index < fixture.Count; index++)
        {
            if (decisions[index * RouterFixture.ResultWords + 4] != fixture.Tables[3])
            {
                throw new InvalidDataException("Stale routing snapshot rejected before actuation.");
            }
        }
        fixture.Packets.CopyTo(rewrittenFrames, 0);
        int queuedCount = 0;
        ulong digest = 0;
        for (int index = 0; index < fixture.Count; index++)
        {
            int result = index * RouterFixture.ResultWords;
            uint status = decisions[result];
            if (status == 0)
            {
                int start = index * RouterFixture.FrameBytes;
                rewrittenFrames[start + 22] = (byte)(decisions[result + 2] >> 16);
                RouterFixture.Write16(rewrittenFrames.AsSpan(start), 24, decisions[result + 2]);
            }
            else if (status == 2)
            {
                queued[queuedCount++] = index;
            }
            digest += status + decisions[result + 1] + (ulong)decisions[result + 2] + decisions[result + 3];
        }
        return digest + (ulong)queuedCount;
    }
}
