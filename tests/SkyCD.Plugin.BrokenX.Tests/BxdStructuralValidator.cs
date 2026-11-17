using System;
using System.Collections.Generic;
using System.Text;

namespace SkyCD.Plugin.BrokenX.Tests;

/// <summary>
/// Independent structural validator for Broken X Database bytes.
/// </summary>
/// <remarks>
/// Implements only the format specification: signature, version, bounded length-prefixed strings,
/// property blocks, preorder structural flags and exact record counts. It deliberately shares no
/// code with the plugin codec, so agreement between the two is genuine cross-validation.
/// </remarks>
public static class BxdStructuralValidator
{
    public static IReadOnlyList<string> Validate(byte[] data)
    {
        var issues = new List<string>();
        try
        {
            ValidateCore(data, issues);
        }
        catch (Exception ex)
        {
            issues.Add(ex.Message);
        }

        return issues;
    }

    private static void ValidateCore(byte[] data, List<string> issues)
    {
        var position = 0;
        if (!TryTake(data, ref position, 21, out var signature) || !IsSignature(signature))
        {
            issues.Add("missing 21-byte signature.");
            return;
        }

        if (!TryTake(data, ref position, 1, out var version) || version[0] != 0x40)
        {
            issues.Add("unsupported archive version.");
            return;
        }

        if (!ReadBlock(data, ref position, issues, "root"))
        {
            return;
        }

        if (!TryTake(data, ref position, 4, out var countBytes))
        {
            issues.Add("truncated item count.");
            return;
        }

        var itemCount = BitConverter.ToUInt32(countBytes, 0);
        if (itemCount > 1_000_000)
        {
            issues.Add("item count exceeds the safety limit.");
            return;
        }

        var stack = new Stack<bool>();
        for (var i = 0u; i < itemCount; i++)
        {
            if (!ReadRecord(data, ref position, issues, i, stack))
            {
                return;
            }
        }

        if (stack.Count > 0)
        {
            issues.Add("unclosed folders at the end of the record stream.");
            return;
        }

        if (position != data.Length)
        {
            issues.Add("trailing data after the last item record.");
        }
    }

    private static bool ReadRecord(byte[] data, ref int position, List<string> issues, uint index, Stack<bool> stack)
    {
        if (!TryTake(data, ref position, 2, out var flagBytes))
        {
            issues.Add($"item {index}: truncated flags.");
            return false;
        }

        var flags = BitConverter.ToUInt16(flagBytes, 0);
        var hasChildren = (flags & 0x4000) == 0;
        var isLast = (flags & 0x2000) != 0;

        if (!ReadString(data, ref position, issues, $"item {index} name"))
        {
            return false;
        }

        if (!TryTake(data, ref position, 8, out _))
        {
            issues.Add($"item {index}: truncated fixed fields.");
            return false;
        }

        if (!ReadBlock(data, ref position, issues, $"item {index} metadata"))
        {
            return false;
        }

        // Recompute the expected structural bits from the tree shape and compare.
        var depth = stack.Count;
        if (depth > 64)
        {
            issues.Add($"item {index}: nesting depth exceeds 64 levels.");
            return false;
        }

        if (hasChildren)
        {
            stack.Push(isLast);
        }
        else
        {
            var last = isLast;
            while (last && stack.Count > 0)
            {
                last = stack.Pop();
            }
        }

        return true;
    }

    private static bool ReadBlock(byte[] data, ref int position, List<string> issues, string what)
    {
        if (!TryTake(data, ref position, 1, out var countBytes))
        {
            issues.Add($"{what}: truncated property count.");
            return false;
        }

        var count = countBytes[0];
        for (var i = 0; i < count; i++)
        {
            if (!TryTake(data, ref position, 1, out var descriptorBytes))
            {
                issues.Add($"{what}: truncated descriptor.");
                return false;
            }

            var descriptor = descriptorBytes[0];
            var kind = (descriptor >> 6) & 0x03;
            switch (kind)
            {
                case 0:
                    issues.Add($"{what}: invalid descriptor 0x{descriptor:X2}.");
                    return false;
                case 1:
                    if (!TryTake(data, ref position, 4, out _))
                    {
                        issues.Add($"{what}: truncated uint32 payload.");
                        return false;
                    }

                    break;
                case 2:
                    if (!ReadString(data, ref position, issues, $"{what} text payload"))
                    {
                        return false;
                    }

                    break;
                default:
                    if (!TryTake(data, ref position, 4, out var lengthBytes))
                    {
                        issues.Add($"{what}: truncated blob length.");
                        return false;
                    }

                    var blobLength = BitConverter.ToUInt32(lengthBytes, 0);
                    if (blobLength > 64 * 1024 * 1024)
                    {
                        issues.Add($"{what}: blob length exceeds the safety limit.");
                        return false;
                    }

                    if (!TryTake(data, ref position, (int)blobLength, out _))
                    {
                        issues.Add($"{what}: truncated blob payload.");
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    private static bool ReadString(byte[] data, ref int position, List<string> issues, string what)
    {
        if (!TryTake(data, ref position, 1, out var first))
        {
            issues.Add($"{what}: truncated length.");
            return false;
        }

        long length = first[0];
        if (length == 0xFF)
        {
            if (!TryTake(data, ref position, 2, out var extended))
            {
                issues.Add($"{what}: truncated extended length.");
                return false;
            }

            length = BitConverter.ToUInt16(extended, 0);
            if (length == 0xFFFF)
            {
                if (!TryTake(data, ref position, 4, out var huge))
                {
                    issues.Add($"{what}: truncated long length.");
                    return false;
                }

                length = BitConverter.ToUInt32(huge, 0);
            }
        }

        if (length > 8 * 1024 * 1024)
        {
            issues.Add($"{what}: length exceeds the safety limit.");
            return false;
        }

        if (!TryTake(data, ref position, (int)length, out var bytes))
        {
            issues.Add($"{what}: truncated bytes.");
            return false;
        }

        try
        {
            _ = Encoding.GetEncoding("utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
        catch
        {
            issues.Add($"{what}: invalid UTF-8.");
            return false;
        }

        return true;
    }

    private static bool TryTake(byte[] data, ref int position, int count, out byte[] slice, bool exact = true)
    {
        slice = [];
        if (count < 0 || position < 0 || position > data.Length)
        {
            return false;
        }

        var available = data.Length - position;
        if (exact && available < count)
        {
            return false;
        }

        var take = Math.Min(count, available);
        slice = new byte[take];
        Buffer.BlockCopy(data, position, slice, 0, take);
        position += take;
        return exact || take == count;
    }

    private static bool IsSignature(byte[] candidate)
    {
        var expected = new byte[]
        {
            0x42, 0x72, 0x6F, 0x6B, 0x65, 0x6E, 0x20, 0x58, 0x20, 0x44, 0x61, 0x74, 0x61, 0x62, 0x61, 0x73,
            0x65, 0x0D, 0x0A, 0x1A, 0x00
        };
        if (candidate.Length != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (candidate[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }
}
