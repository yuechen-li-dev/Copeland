namespace Aurelian.Graphics.Vulkan.Pipelines.Graphics;

/// <summary>Binary framing checks before native submission, not semantic SPIR-V validation.</summary>
internal static class SpirvModuleStructure
{
    public static bool IsWellFormed(IReadOnlyList<uint> words)
    {
        // SPIR-V physical layout: five header words followed by word-count-prefixed instructions.
        // https://registry.khronos.org/SPIR-V/specs/unified1/SPIRV.html#_physical_layout_of_a_spir_v_module_and_instruction
        if (words.Count <= 5 || words[0] != 0x07230203 || words[1] < 0x00010000
            || words[1] > 0x00010600 || (words[1] & 0xff) != 0 || words[3] == 0 || words[4] != 0)
        {
            return false;
        }
        bool memoryModel = false;
        bool entryPoint = false;
        int offset = 5;
        while (offset < words.Count)
        {
            int length = (int)(words[offset] >> 16);
            uint opcode = words[offset] & 0xffff;
            if (length == 0 || length > words.Count - offset)
            {
                return false;
            }
            if (opcode == 14)
            {
                if (length != 3)
                {
                    return false;
                }
                memoryModel = true;
            }
            if (opcode == 15)
            {
                if (length < 4)
                {
                    return false;
                }
                entryPoint = true;
            }
            offset += length;
        }
        return memoryModel && entryPoint;
    }
}
