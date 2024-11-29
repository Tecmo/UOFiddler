using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ultima.Helpers
{
    public static class BwtDecompress
    {
        public static byte[] Decompress(byte[] buffer)
        {
            byte[] output = null;

            using (var reader = new BinaryReader(new MemoryStream(buffer)))
            {
                var header = reader.ReadUInt32();
                Console.WriteLine($"Header: {header} (0x{header:X8})");

                var len = 0u;

                var firstChar = reader.ReadByte();
                Console.WriteLine($"First character read: {firstChar} (0x{firstChar:X2})");

                Span<ushort> table = new ushort[256 * 256];
                BuildTable(table, firstChar);

                var list = new byte[reader.BaseStream.Length - 4];
                var i = 0;
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    var currentValue = firstChar;
                    var value = table[currentValue];
                    if (currentValue > 0)
                    {
                        do
                        {
                            table[currentValue] = table[currentValue - 1];
                        } while (--currentValue > 0);
                    }

                    table[0] = value;

                    list[i++] = (byte)value;
                    firstChar = reader.ReadByte();
                }

                Console.WriteLine($"Remaining bytes: {reader.BaseStream.Length - reader.BaseStream.Position}");


                output = InternalDecompress(list, len);
            }

            return output;
        }

        static void BuildTable(Span<ushort> table, byte startValue)
        {
            int index = 0;
            byte firstByte = startValue;
            byte secondByte = 0;
            for (int i = 0; i < 256 * 256; i++)
            {
                var val = (ushort)(firstByte + (secondByte << 8));
                table[index++] = val;

                firstByte++;
                if (firstByte == 0)
                {
                    secondByte++;
                }
            }

            table.Sort();

            // Debug the first few entries of the table
            //Console.WriteLine($"Table (First 20 values): {string.Join(", ", table[..Math.Min(20, table.Length)].ToArray().Select(x => x.ToString()))}");
        }

        static byte[] InternalDecompress(Span<byte> input, uint len)
        {
            try
            {
                //Console.WriteLine($"Decompression: Input length = {input.Length}, Expected length = {len}");

                Span<char> symbolTable = stackalloc char[256];
                Span<char> frequency = stackalloc char[256];
                Span<int> partialInput = stackalloc int[256 * 3];
                partialInput.Clear();

                for (var i = 0; i < 256; i++)
                    symbolTable[i] = (char)i;

                input.Slice(0, 1024).CopyTo(MemoryMarshal.AsBytes(partialInput));

                var sum = 0;
                for (var i = 0; i < 256; i++)
                    sum += partialInput[i];

                if (len == 0)
                {
                    len = (uint)sum;
                }

                if (sum != len)
                    return Array.Empty<byte>();

                var output = new byte[len];

                var count = 0;
                var nonZeroCount = 0;

                for (var i = 0; i < 256; i++)
                {
                    if (partialInput[i] != 0)
                        nonZeroCount++;
                }

                Frequency(partialInput, frequency);

                for (int i = 0, m = 0; i < nonZeroCount; ++i)
                {
                    var freq = (byte)frequency[i];
                    symbolTable[input[m + 1024]] = (char)freq;
                    partialInput[freq + 256] = m + 1;
                    m += partialInput[freq];
                    partialInput[freq + 512] = m;
                }

                var val = (byte)symbolTable[0];

                if (len != 0)
                {
                    do
                    {
                        ref var firstValRef = ref partialInput[val + 256];
                        output[count] = val;

                        if (firstValRef >= partialInput[val + 512])
                        {
                            if (nonZeroCount-- > 0)
                            {
                                ShiftLeft(symbolTable, nonZeroCount);
                                val = (byte)symbolTable[0];
                            }
                        }
                        else
                        {
                            var idx = (char)input[firstValRef + 1024];
                            firstValRef++;

                            if (idx != 0)
                            {
                                ShiftLeft(symbolTable, idx);
                                symbolTable[(byte)idx] = (char)val;
                                val = (byte)symbolTable[0];
                            }
                        }

                        count++;
                    } while (count < len);
                }

                //Console.WriteLine($"Input length: {input.Length}, Expected length: {len}");
                //Console.WriteLine($"Partial input values: {string.Join(", ", input.Slice(0, Math.Min(input.Length, 20)).ToArray())}");


                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during decompression: {ex.Message}");
                throw;
            }
        }

        static void Frequency(Span<int> input, Span<char> output)
        {
            Span<int> tmp = stackalloc int[256];
            input.Slice(0, tmp.Length).CopyTo(tmp);

            for (var i = 0; i < 256; i++)
            {
                uint value = 0;
                byte index = 0;

                for (var j = 0; j < 256; j++)
                {
                    if (tmp[j] > value)
                    {
                        index = (byte)j;
                        value = (uint)tmp[j];
                    }
                }

                if (value == 0)
                    break;

                output[i] = (char)index;
                tmp[index] = 0;
            }
        }

        static void ShiftLeft(Span<char> input, int max)
        {
            for (var i = 0; i < max; ++i)
                input[i] = input[i + 1];
        }
    }
}