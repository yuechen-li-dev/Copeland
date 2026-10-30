using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Aurelian.RouterResearch;

internal sealed record PolicyRule(uint Source, uint SourceMask, uint Destination,
    uint DestinationMask, uint PortLow, uint PortHigh, uint Protocol, uint Deny);

internal sealed class RouterFixture
{
    public const int FrameBytes = 64;
    public const int ResultWords = 5;
    public const uint Missing = uint.MaxValue;
    public int Count { get; }
    public int Candidates { get; }
    public PolicyRule[] Rules { get; }
    public uint[] Tables { get; }
    public byte[] Packets { get; }

    public RouterFixture(int count, int candidateCount, int ruleCount, int seed, string distribution = "mixed")
    {
        Count = count;
        Candidates = candidateCount;
        var random = new Random(seed);
        Rules = Enumerable.Range(0, ruleCount).Select(index => MakeRule(random, index)).ToArray();
        Tables = MakeTables();
        CompileRoutes();
        Packets = new byte[count * FrameBytes];
        for (int index = 0; index < count; index++)
        {
            var frame = Packets.AsSpan(index * FrameBytes, FrameBytes);
            uint source = 0x0A000000u | (uint)random.Next(1 << 24);
            uint destination = 0x0A000000u | (uint)random.Next(1 << 16);
            uint port = (uint)random.Next(65536);
            uint protocol = index % 2 == 0 ? 6u : 17u;
            int selectedRule = -1;
            if (ruleCount > 0)
            {
                if (distribution == "early" || (distribution == "mixed" && index % 5 == 1))
                {
                    selectedRule = 0;
                }
                else if (distribution == "late" || (distribution == "mixed" && index % 5 == 2))
                {
                    selectedRule = ruleCount - 1;
                }
            }
            if (selectedRule >= 0)
            {
                var rule = Rules[selectedRule];
                source = rule.Source | (source & ~rule.SourceMask);
                destination = rule.Destination | (destination & ~rule.DestinationMask);
                port = rule.PortLow;
                if (rule.Protocol != 0)
                {
                    protocol = rule.Protocol;
                }
            }
            Write16(frame, 12, 0x0800);
            frame[14] = 0x45;
            Write16(frame, 16, protocol == 6 ? 40u : 28u);
            frame[22] = index % 67 == 66 ? (byte)1 : (byte)64;
            frame[23] = (byte)protocol;
            Write32(frame, 26, source);
            Write32(frame, 30, destination);
            Write16(frame, 34, (uint)random.Next(65536));
            Write16(frame, 36, port);
            if (protocol == 17)
            {
                Write16(frame, 38, 8);
            }
            else
            {
                frame[46] = 0x50;
            }
            Write16(frame, 24, HeaderChecksum(frame));
            if (index % 127 == 1)
            {
                frame[14] = 0x46;
            }
        }
    }

    private static PolicyRule MakeRule(Random random, int index)
    {
        uint sourceMask = index % 4 == 0 ? 0xFF000000u : 0xFFFF0000u;
        uint destinationMask = index % 3 == 0 ? 0xFFFF0000u : 0xFFFFFF00u;
        uint source = (0x0A000000u | (uint)random.Next(1 << 24)) & sourceMask;
        uint destination = (0x0A000000u | (uint)random.Next(1 << 16)) & destinationMask;
        uint low = (uint)random.Next(65536);
        uint high = Math.Min(65535u, low + (uint)random.Next(8192));
        uint protocol = index % 3 == 0 ? 0u : (index % 2 == 0 ? 6u : 17u);
        return new(source, sourceMask, destination, destinationMask, low, high, protocol, (uint)(index % 2));
    }

    private uint[] MakeTables()
    {
        // Header: packet count, candidate count, rule count, generation, route base,
        // compiled-route base, index-header base, maximum bucket size.
        var data = new List<uint>(Enumerable.Repeat(0u, 8 + 256 * Candidates * 3 + 256 + 32 * 3));
        data[0] = (uint)Count;
        data[1] = (uint)Candidates;
        data[2] = (uint)Rules.Length;
        data[3] = 1;
        data[4] = 8;
        data[5] = (uint)(8 + 256 * Candidates * 3);
        data[6] = data[5] + 256;
        for (int group = 0; group < 256; group++)
        {
            for (int candidate = 0; candidate < Candidates; candidate++)
            {
                int offset = 8 + (group * Candidates + candidate) * 3;
                data[offset] = (uint)(Candidates - candidate);
                data[offset + 1] = group % 13 != 0 && (group + candidate) % 7 != 0 ? 1u : 0u;
                data[offset + 2] = group % 17 == 0 ? 20u : (uint)((group * 7 + candidate * 19) % 101);
            }
        }
        for (int bucket = 0; bucket < 32; bucket++)
        {
            uint protocol = bucket / 16 == 0 ? 6u : 17u;
            uint low = (uint)(bucket % 16 * 4096);
            uint high = low + 4095;
            int[] indices = Enumerable.Range(0, Rules.Length).Where(index =>
                (Rules[index].Protocol == 0 || Rules[index].Protocol == protocol)
                && Rules[index].PortLow <= high && Rules[index].PortHigh >= low).ToArray();
            if (indices.Length > 512)
            {
                throw new InvalidDataException("Policy bucket exceeds the shader's explicit 512-rule bound.");
            }
            int stride = Math.Max(8, (indices.Length + 7) / 8 * 8);
            int offset = data.Count;
            int header = (int)data[6] + bucket * 3;
            data[header] = (uint)offset;
            data[header + 1] = (uint)indices.Length;
            data[header + 2] = (uint)stride;
            data[7] = Math.Max(data[7], (uint)indices.Length);
            for (int field = 0; field < 8; field++)
            {
                for (int lane = 0; lane < stride; lane++)
                {
                    uint value = 0;
                    if (lane < indices.Length)
                    {
                        int index = indices[lane];
                        var rule = Rules[index];
                        value = field switch
                        {
                            0 => rule.Source,
                            1 => rule.SourceMask,
                            2 => rule.Destination,
                            3 => rule.DestinationMask,
                            4 => rule.PortLow,
                            5 => rule.PortHigh,
                            6 => rule.Deny,
                            7 => (uint)index,
                            _ => throw new InvalidOperationException(),
                        };
                    }
                    data.Add(value);
                }
            }
        }
        return data.ToArray();
    }

    public void CompileRoutes()
    {
        for (int group = 0; group < 256; group++)
        {
            uint bestPort = Missing;
            uint bestCongestion = Missing;
            for (int candidate = 0; candidate < Candidates; candidate++)
            {
                int entry = (int)Tables[4] + (group * Candidates + candidate) * 3;
                uint port = Tables[entry];
                uint congestion = Tables[entry + 2];
                if (Tables[entry + 1] == 1 && (congestion < bestCongestion
                    || (congestion == bestCongestion && port < bestPort)))
                {
                    bestPort = port;
                    bestCongestion = congestion;
                }
            }
            Tables[(int)Tables[5] + group] = bestPort;
        }
    }

    public byte[] TableBytes() => MemoryMarshal.AsBytes(Tables.AsSpan()).ToArray();

    public static uint Read16(ReadOnlySpan<byte> frame, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(frame[offset..]);
    }

    public static uint Read32(ReadOnlySpan<byte> frame, int offset)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(frame[offset..]);
    }

    public static void Write16(Span<byte> frame, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(frame[offset..], (ushort)value);
    }

    private static void Write32(Span<byte> frame, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(frame[offset..], value);
    }

    public static uint HeaderChecksum(ReadOnlySpan<byte> frame)
    {
        uint sum = 0;
        for (int word = 0; word < 10; word++)
        {
            sum += Read16(frame, 14 + word * 2);
        }
        sum = (sum & 65535) + (sum >> 16);
        sum = (sum & 65535) + (sum >> 16);
        return ~sum & 65535;
    }
}
