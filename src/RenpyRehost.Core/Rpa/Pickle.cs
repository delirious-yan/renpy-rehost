using System.Text;

namespace RenpyRehost.Core.Rpa;

/// <summary>
/// A deliberately tiny pickle reader — just the opcodes a Ren'Py archive index
/// uses (a dict of <c>path -&gt; [(offset, length, prefix), …]</c>). Not a general
/// unpickler; it throws on anything outside that shape.
/// </summary>
public static class Pickle
{
    public static object? Loads(byte[] data)
    {
        var stack = new List<object?>();
        var memo = new Dictionary<int, object?>();
        int i = 0;

        // MARK is represented by this sentinel on the stack
        object mark = new();

        while (i < data.Length)
        {
            byte op = data[i++];
            switch ((char)op)
            {
                case '\x80': i++; break;                         // PROTO
                case '\x95': i += 8; break;                      // FRAME
                case '(': stack.Add(mark); break;                // MARK
                case '}': stack.Add(new Dictionary<object, object?>()); break; // EMPTY_DICT
                case ']': stack.Add(new List<object?>()); break; // EMPTY_LIST
                case ')': stack.Add(Array.Empty<object?>()); break; // EMPTY_TUPLE
                case '.': return stack[^1];                      // STOP

                case 'X': stack.Add(ReadStr(data, ref i, ReadI32(data, ref i))); break;      // BINUNICODE
                case '\x8c': stack.Add(ReadStr(data, ref i, data[i++])); break;              // SHORT_BINUNICODE
                case 'U': stack.Add(ReadBytes(data, ref i, data[i++])); break;               // SHORT_BINSTRING
                case 'T': stack.Add(ReadBytes(data, ref i, ReadI32(data, ref i))); break;    // BINSTRING
                case 'C': stack.Add(ReadBytes(data, ref i, data[i++])); break;               // SHORT_BINBYTES
                case 'B': stack.Add(ReadBytes(data, ref i, ReadI32(data, ref i))); break;    // BINBYTES

                case 'K': stack.Add((long)data[i++]); break;                                 // BININT1
                case 'M': stack.Add((long)(data[i] | data[i + 1] << 8)); i += 2; break;      // BININT2
                case 'J': stack.Add((long)ReadI32(data, ref i)); break;                      // BININT
                case '\x8a': stack.Add(ReadLong(data, ref i, data[i++])); break;             // LONG1
                case '\x8b': stack.Add(ReadLong(data, ref i, ReadI32(data, ref i))); break;  // LONG4

                case '\x85': { var a = Pop(stack); stack.Add(new object?[] { a }); break; }               // TUPLE1
                case '\x86': { var b = Pop(stack); var a = Pop(stack); stack.Add(new object?[] { a, b }); break; } // TUPLE2
                case '\x87': { var c = Pop(stack); var b = Pop(stack); var a = Pop(stack); stack.Add(new object?[] { a, b, c }); break; } // TUPLE3
                case 't': { var items = PopToMark(stack, mark); stack.Add(items.ToArray()); break; }       // TUPLE

                case 'a': { var v = Pop(stack); ((List<object?>)stack[^1]!).Add(v); break; }               // APPEND
                case 'e': { var items = PopToMark(stack, mark); ((List<object?>)stack[^1]!).AddRange(items); break; } // APPENDS

                case 's': { var v = Pop(stack); var k = Pop(stack); ((Dictionary<object, object?>)stack[^1]!)[k!] = v; break; } // SETITEM
                case 'u': // SETITEMS
                {
                    var items = PopToMark(stack, mark);
                    var dict = (Dictionary<object, object?>)stack[^1]!;
                    for (int j = 0; j + 1 < items.Count; j += 2) dict[items[j]!] = items[j + 1];
                    break;
                }

                case 'q': memo[data[i++]] = stack[^1]; break;                                 // BINPUT
                case 'r': memo[ReadI32(data, ref i)] = stack[^1]; break;                      // LONG_BINPUT
                case '\x94': memo[memo.Count] = stack[^1]; break;                             // MEMOIZE
                case 'h': stack.Add(memo[data[i++]]); break;                                  // BINGET
                case 'j': stack.Add(memo[ReadI32(data, ref i)]); break;                       // LONG_BINGET

                default:
                    throw new RehostException(
                        $"Unsupported pickle opcode 0x{op:x2} ('{(char)op}') at {i - 1} while reading the archive index.");
            }
        }
        throw new RehostException("Pickle stream ended without STOP.");
    }

    private static object? Pop(List<object?> s) { var v = s[^1]; s.RemoveAt(s.Count - 1); return v; }

    private static List<object?> PopToMark(List<object?> s, object mark)
    {
        int idx = s.LastIndexOf(mark);
        var items = s.GetRange(idx + 1, s.Count - idx - 1);
        s.RemoveRange(idx, s.Count - idx);
        return items;
    }

    private static int ReadI32(byte[] d, ref int i)
    {
        int v = d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24;
        i += 4;
        return v;
    }

    private static long ReadLong(byte[] d, ref int i, int len)
    {
        long v = 0;
        for (int k = 0; k < len; k++) v |= (long)d[i + k] << (8 * k);
        if (len > 0 && (d[i + len - 1] & 0x80) != 0) v -= 1L << (8 * len); // sign-extend
        i += len;
        return v;
    }

    // BINUNICODE — archive paths.
    private static string ReadStr(byte[] d, ref int i, int len)
    {
        string s = Encoding.UTF8.GetString(d, i, len);
        i += len;
        return s;
    }

    // BINSTRING / BINBYTES — the tuple prefix, which is raw bytes (usually empty).
    private static byte[] ReadBytes(byte[] d, ref int i, int len)
    {
        var b = new byte[len];
        Array.Copy(d, i, b, 0, len);
        i += len;
        return b;
    }
}
