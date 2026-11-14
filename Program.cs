
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class ARM32VM
{
    const int MEM_SIZE = 1024 * 1024;
    const int TEXT_START = 0x10000;
    const int DATA_START = 0x20000;
    const int STACK_TOP = 0x80000;

    public int[] R = new int[16];
    public byte[] Memory = new byte[MEM_SIZE];

    public Dictionary<string, int> Labels = new Dictionary<string, int>();
    public List<(string code, int address, int line)> Instructions =
        new List<(string, int, int)>();

    public int N, Z, C, V;
    public bool Halted;
    public int ExitCode;
    public StringBuilder Output = new StringBuilder();

    public ARM32VM()
    {
        Reset();
    }

    public void Reset()
    {
        Array.Clear(R, 0, R.Length);
        Array.Clear(Memory, 0, Memory.Length);

        Labels.Clear();
        Instructions.Clear();
        Output.Clear();

        N = Z = C = V = 0;
        Halted = false;
        ExitCode = 0;

        R[13] = STACK_TOP;
        R[15] = TEXT_START;
    }

    // Memory operations

    public void Write8(uint addr, int value)
    {
        if (addr < MEM_SIZE)
            Memory[addr] = (byte)value;
    }

    public byte Read8(uint addr)
    {
        return addr < MEM_SIZE ? Memory[addr] : (byte)0;
    }

    public void Write32(uint addr, int value)
    {
        Write8(addr, value);
        Write8(addr + 1, value >> 8);
        Write8(addr + 2, value >> 16);
        Write8(addr + 3, value >> 24);
    }

    public int Read32(uint addr)
    {
        return Read8(addr)
            | (Read8(addr + 1) << 8)
            | (Read8(addr + 2) << 16)
            | (Read8(addr + 3) << 24);
    }

    // Number parser

    static int Number(string s)
    {
        s = s.Trim();

        bool negative = s.StartsWith("-");
        if (negative || s.StartsWith("+"))
            s = s.Substring(1);

        long value;

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = long.Parse(s.Substring(2), NumberStyles.HexNumber);
        else
            value = long.Parse(s, CultureInfo.InvariantCulture);

        if (negative) value = -value;

        return unchecked((int)value);
    }

    // Register parser

    int ParseReg(string s)
    {
        s = s.Trim().ToLowerInvariant();

        if (s == "sp") return 13;
        if (s == "lr") return 14;
        if (s == "pc") return 15;

        if (s.StartsWith("r") &&
            int.TryParse(s.Substring(1), out int n) &&
            n >= 0 && n <= 15)
            return n;

        return -1;
    }

    int Operand(string s)
    {
        s = s.Trim();

        if (s.StartsWith("#"))
            return Number(s.Substring(1));

        if (s.StartsWith("="))
        {
            string name = s.Substring(1);

            if (Labels.ContainsKey(name))
                return Labels[name];

            return Number(name);
        }

        int reg = ParseReg(s);

        if (reg >= 0)
            return R[reg];

        if (Labels.ContainsKey(s))
            return Labels[s];

        return Number(s);
    }

    // String decoder

    string DecodeString(string s)
    {
        return s.Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\r", "\r")
                .Replace("\\0", "\0")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
    }

    // Assembly loader

    public void Load(string source)
    {
        Reset();

        string[] lines = source.Replace("\r\n", "\n")
                               .Replace('\r', '\n')
                               .Split('\n');

        string section = ".text";

        int dataPtr = DATA_START;
        int textPtr = TEXT_START;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = Regex.Replace(lines[i], @"(@|//|;).*$", "").Trim();

            if (line.Length == 0)
                continue;

            // Labels

            if (line.Contains(":"))
            {
                int pos = line.IndexOf(':');

                string label = line.Substring(0, pos).Trim();

                Labels[label] = section == ".data" ? dataPtr : textPtr;

                line = line.Substring(pos + 1).Trim();

                if (line.Length == 0)
                    continue;
            }

            // Directives

            if (line.StartsWith("."))
            {
                if (line.StartsWith(".data"))
                    section = ".data";

                else if (line.StartsWith(".text"))
                    section = ".text";

                else if (line.StartsWith(".ascii") ||
                         line.StartsWith(".asciz") ||
                         line.StartsWith(".string"))
                {
                    Match m = Regex.Match(line, "\"(.*)\"");

                    if (m.Success)
                    {
                        string value = DecodeString(m.Groups[1].Value);

                        foreach (char c in value)
                            Write8((uint)dataPtr++, c);

                        if (!line.StartsWith(".ascii"))
                            Write8((uint)dataPtr++, 0);
                    }
                }

                else if (line.StartsWith(".word"))
                {
                    string[] values = line.Substring(5).Split(',');

                    foreach (string value in values)
                    {
                        Write32((uint)dataPtr, Number(value));
                        dataPtr += 4;
                    }
                }

                else if (line.StartsWith(".byte"))
                {
                    string[] values = line.Substring(5).Split(',');

                    foreach (string value in values)
                        Write8((uint)dataPtr++, Number(value));
                }

                else if (line.StartsWith(".space") ||
                         line.StartsWith(".skip"))
                {
                    string[] p = line.Split(
                        (char[])null,
                        StringSplitOptions.RemoveEmptyEntries);

                    int size = p.Length > 1 ? Number(p[1]) : 1;

                    dataPtr += Math.Max(0, size);
                }

                continue;
            }

            // Instructions

            if (section == ".text")
            {
                Instructions.Add((line, textPtr, i + 1));
                textPtr += 4;
            }
        }

        R[15] = Labels.ContainsKey("_start")
            ? Labels["_start"]
            : TEXT_START;
    }

    // CPSR conditions

    bool Condition(string c)
    {
        switch (c)
        {
            case "eq": return Z == 1;
            case "ne": return Z == 0;
            case "cs":
            case "hs": return C == 1;
            case "cc":
            case "lo": return C == 0;
            case "mi": return N == 1;
            case "pl": return N == 0;
            case "vs": return V == 1;
            case "vc": return V == 0;
            case "hi": return C == 1 && Z == 0;
            case "ls": return C == 0 || Z == 1;
            case "ge": return N == V;
            case "lt": return N != V;
            case "gt": return Z == 0 && N == V;
            case "le": return Z == 1 || N != V;
            default: return true;
        }
    }

    void UpdateFlags(int result, int a, int b, bool sub)
    {
        Z = result == 0 ? 1 : 0;
        N = (result & unchecked((int)0x80000000)) != 0 ? 1 : 0;

        if (sub)
        {
            C = (uint)a >= (uint)b ? 1 : 0;

            V = (((a ^ b) & (a ^ result) &
                unchecked((int)0x80000000)) != 0) ? 1 : 0;
        }
        else
        {
            C = (ulong)(uint)a + (uint)b > uint.MaxValue ? 1 : 0;

            V = ((~(a ^ b) & (a ^ result) &
                unchecked((int)0x80000000)) != 0) ? 1 : 0;
        }
    }

    // Linux ARM syscalls

    void Syscall()
    {
        int syscall = R[7];

        switch (syscall)
        {
            case 1: // sys_exit

                ExitCode = R[0];
                Halted = true;

                Output.AppendLine();
                Output.AppendLine(
                    "[Program terminated with exit code: " + ExitCode + "]");

                break;

            case 4: // sys_write

                uint address = (uint)R[1];
                int count = Math.Max(0, R[2]);

                if (R[0] != 1 && R[0] != 2)
                {
                    R[0] = -9;
                    break;
                }

                for (int i = 0; i < count; i++)
                    Output.Append((char)Read8(address + (uint)i));

                R[0] = count;

                break;

            default:

                Output.AppendLine();
                Output.AppendLine("[Unknown syscall: #" + syscall + "]");

                Halted = true;

                break;
        }
    }

    // Execute one ARM instruction

    public bool Step()
    {
        if (Halted)
            return false;

        int pc = R[15];

        var inst = Instructions.FirstOrDefault(
            x => x.address == pc);

        if (inst.code == null)
        {
            Halted = true;

            Output.AppendLine();
            Output.AppendLine(
                "[Program halted: PC reached end of instructions]");

            return false;
        }

        R[15] += 4;

        string line = inst.code;

        string[] t = Regex.Split(line.Trim(), @"[,\s]+")
                          .Where(x => x.Length > 0)
                          .ToArray();

        string op = t[0].ToLowerInvariant();

        bool setFlags = false;
        string cond = "";

        string[] conditions = {
            "eq", "ne", "cs", "hs", "cc", "lo",
            "mi", "pl", "vs", "vc", "hi", "ls",
            "ge", "lt", "gt", "le"
        };

        if (op.StartsWith("b"))
        {
            foreach (string c in conditions)
            {
                if (op == "b" + c)
                {
                    cond = c;
                    op = "b";
                    break;
                }
            }
        }

        if (op == "subs" || op == "adds")
        {
            setFlags = true;
            op = op.Substring(0, op.Length - 1);
        }
        else if (op.EndsWith("s") &&
                 op != "push" && op != "svc" && op != "swi")
        {
            setFlags = true;
            op = op.Substring(0, op.Length - 1);
        }

        if (cond != "" && !Condition(cond))
            return true;

        switch (op)
        {
            case "mov":
            {
                int rd = ParseReg(t[1]);
                int value = Operand(t[2]);

                R[rd] = value;

                if (setFlags)
                    UpdateFlags(value, 0, 0, false);

                break;
            }

            case "mvn":
            {
                int rd = ParseReg(t[1]);
                int value = ~Operand(t[2]);

                R[rd] = value;

                if (setFlags)
                    UpdateFlags(value, 0, 0, false);

                break;
            }

            case "add":
            case "sub":
            {
                int rd = ParseReg(t[1]);
                int a = Operand(t[2]);
                int b = t.Length > 3 ? Operand(t[3]) : a;

                int result = op == "add"
                    ? unchecked(a + b)
                    : unchecked(a - b);

                R[rd] = result;

                if (setFlags)
                    UpdateFlags(result, a, b, op == "sub");

                break;
            }

            case "cmp":
            {
                int a = Operand(t[1]);
                int b = Operand(t[2]);

                UpdateFlags(unchecked(a - b), a, b, true);

                break;
            }

            case "b":
            {
                if (Labels.ContainsKey(t[1]))
                    R[15] = Labels[t[1]];

                break;
            }

            case "bl":
            {
                R[14] = R[15];

                if (Labels.ContainsKey(t[1]))
                    R[15] = Labels[t[1]];

                break;
            }

            case "bx":
            {
                int reg = ParseReg(t[1]);

                R[15] = R[reg];

                break;
            }

            case "push":
            {
                Match m = Regex.Match(line, @"\{([^}]+)\}");

                if (m.Success)
                {
                    var regs = m.Groups[1].Value.Split(',')
                        .Select(ParseReg)
                        .Where(x => x >= 0)
                        .OrderByDescending(x => x);

                    foreach (int reg in regs)
                    {
                        R[13] -= 4;
                        Write32((uint)R[13], R[reg]);
                    }
                }

                break;
            }

            case "pop":
            {
                Match m = Regex.Match(line, @"\{([^}]+)\}");

                if (m.Success)
                {
                    var regs = m.Groups[1].Value.Split(',')
                        .Select(ParseReg)
                        .Where(x => x >= 0)
                        .OrderBy(x => x);

                    foreach (int reg in regs)
                    {
                        R[reg] = Read32((uint)R[13]);
                        R[13] += 4;
                    }
                }

                break;
            }

            case "ldr":
            case "ldrb":
            {
                int rd = ParseReg(t[1]);

                int eq = line.IndexOf('=');

                if (eq >= 0)
                {
                    string operand = line.Substring(eq).Trim();
                    R[rd] = Operand(operand);
                }
                else
                {
                    Match m = Regex.Match(line,
                        @"\[\s*([a-zA-Z0-9]+)(?:\s*,\s*#?(-?\d+))?\s*\]");

                    if (m.Success)
                    {
                        int rn = ParseReg(m.Groups[1].Value);

                        int offset = m.Groups[2].Success
                            ? int.Parse(m.Groups[2].Value)
                            : 0;

                        uint address = unchecked((uint)(R[rn] + offset));

                        R[rd] = op == "ldrb"
                            ? Read8(address)
                            : Read32(address);
                    }
                }

                break;
            }

            case "str":
            case "strb":
            {
                int rd = ParseReg(t[1]);

                Match m = Regex.Match(line,
                    @"\[\s*([a-zA-Z0-9]+)(?:\s*,\s*#?(-?\d+))?\s*\]");

                if (m.Success)
                {
                    int rn = ParseReg(m.Groups[1].Value);

                    int offset = m.Groups[2].Success
                        ? int.Parse(m.Groups[2].Value)
                        : 0;

                    uint address = unchecked((uint)(R[rn] + offset));

                    if (op == "strb")
                        Write8(address, R[rd]);
                    else
                        Write32(address, R[rd]);
                }

                break;
            }

            case "svc":
            case "swi":
                Syscall();
                break;

            default:
                throw new Exception(
                    "Unsupported instruction: " + t[0] +
                    " (line " + inst.line + ")");
        }

        return true;
    }

    // Run entire program

    public void Run()
    {
        int count = 0;

        while (!Halted && count < 1000000)
        {
            Step();
            count++;
        }

        if (!Halted)
        {
            Halted = true;
            Output.AppendLine();
            Output.AppendLine("[Instruction limit reached]");
        }

        Console.WriteLine("Instructions executed: " + count);
    }
}

class Program
{
    static void Main(string[] args)
    {
        Console.BackgroundColor = ConsoleColor.White;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.Clear();
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("======================================");
        Console.WriteLine("       ARM32 LINUX SYSCALL EMULATOR");
        Console.WriteLine("======================================");
        Console.WriteLine();

        string inputFile;

        if (args.Length > 0)
            inputFile = args[0];
        else
        {
            Console.Write("Assembly input file (.s): ");
            inputFile = Console.ReadLine() ?? "";
        }

        try
        {
            if (!File.Exists(inputFile))
                throw new FileNotFoundException("Input file not found.");

            string source = File.ReadAllText(inputFile, Encoding.UTF8);

            ARM32VM vm = new ARM32VM();

            Console.WriteLine();
            Console.WriteLine("[Loading ARM32 assembly...]");

            vm.Load(source);

            Console.WriteLine("[Program loaded]");
            Console.WriteLine();

            Console.WriteLine("[Executing program...]");
            Console.WriteLine();

            vm.Run();

            // Console output

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("             CONSOLE OUTPUT");
            Console.WriteLine("======================================");

            Console.Write(vm.Output.ToString());

            // Registers

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("               REGISTERS");
            Console.WriteLine("======================================");

            for (int i = 0; i < 16; i++)
            {
                string name = "R" + i;

                if (i == 13) name = "SP";
                if (i == 14) name = "LR";
                if (i == 15) name = "PC";

                Console.WriteLine("{0,-5} 0x{1:X8} ({2})",
                    name, unchecked((uint)vm.R[i]), vm.R[i]);
            }

            // CPSR

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("               CPSR FLAGS");
            Console.WriteLine("======================================");

            Console.WriteLine("N: {0}   Z: {1}   C: {2}   V: {3}",
                vm.N, vm.Z, vm.C, vm.V);

            // Memory viewer

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("              MEMORY VIEW");
            Console.WriteLine("======================================");

            Console.WriteLine("Address    Hex Bytes                 ASCII");

            for (int row = 0; row < 8; row++)
            {
                uint address = 0x20000u + (uint)(row * 8);

                StringBuilder hex = new StringBuilder();
                StringBuilder ascii = new StringBuilder();

                for (int col = 0; col < 8; col++)
                {
                    byte b = vm.Read8(address + (uint)col);

                    if (col > 0)
                        hex.Append(" ");

                    hex.Append(b.ToString("X2"));

                    ascii.Append(b >= 32 && b <= 126
                        ? (char)b
                        : '.');
                }

                Console.WriteLine("0x{0:X8} {1,-24} [{2}]",
                    address, hex, ascii);
            }

            // Final status

            Console.WriteLine();
            Console.WriteLine("======================================");
            Console.WriteLine("            EXECUTION STATUS");
            Console.WriteLine("======================================");

            Console.WriteLine("Halted: " + vm.Halted);
            Console.WriteLine("Exit code: " + vm.ExitCode);

            Console.WriteLine();
            Console.WriteLine("Memory map:");
            Console.WriteLine("Text  : 0x00010000");
            Console.WriteLine("Data  : 0x00020000");
            Console.WriteLine("Stack : 0x00080000");

        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("[ERROR] " + ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("Press ENTER to exit...");
        Console.ReadLine();
    }
}
