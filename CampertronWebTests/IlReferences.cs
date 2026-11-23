using System.Reflection;
using System.Reflection.Emit;

namespace CampertronWebTests
{
    // What a type's code refers to, read from its IL (the GUI tests' scanner, GuiTestHelpers, copied): the searches pages
    // must reach no monitoring, search run, notification or deletion (v0.3.0's PR 6)
    internal static class IlReferences
    {
        // The members (as "Type::Member", with their assembly) that the methods of Types and of their nested
        // (compiler-generated) types refer to, read from their IL.
        internal static IEnumerable<(Assembly? Assembly, string Member)> ReferencedMembers(params Type[] Types)
        {
            var All = new List<Type>();
            void Add(Type T)
            {
                All.Add(T);
                foreach (Type Nested in T.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    Add(Nested);
                }
            }
            foreach (Type T in Types.Where(t => !t.IsNested))
            {
                Add(T);
            }
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodBase Method in All.SelectMany(t => t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared))))
            {
                foreach (MemberInfo Member in IlMembers(Method))
                {
                    Type? Owner = Member as Type ?? Member.DeclaringType;
                    yield return (Owner?.Assembly, (Member.DeclaringType?.FullName ?? (Member as Type)?.FullName ?? "?") + "::" + Member.Name);
                }
            }
        }

        // The members (as "Type::Member") that one method refers to, read from its IL.
        internal static List<string> ReferencedMembers(MethodBase Method)
        {
            return IlMembers(Method).Select(Member => (Member.DeclaringType?.FullName ?? (Member as Type)?.FullName ?? "?") + "::" + Member.Name).ToList();
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(F => (OpCode)F.GetValue(null)!).ToDictionary(O => O.Value);

        private static IEnumerable<MemberInfo> IlMembers(MethodBase Method)
        {
            byte[]? IL = Method.GetMethodBody()?.GetILAsByteArray();
            if (IL == null)
            {
                yield break;
            }
            Type[]? TypeArguments = Method.DeclaringType != null && Method.DeclaringType.IsGenericType ? Method.DeclaringType.GetGenericArguments() : null;
            Type[]? MethodArguments = Method.IsGenericMethod ? Method.GetGenericArguments() : null;
            int i = 0;
            while (i < IL.Length)
            {
                short Value = IL[i] == 0xFE ? (short)(0xFE00 | IL[i + 1]) : IL[i];
                i += IL[i] == 0xFE ? 2 : 1;
                OpCode Op = OpCodesByValue[Value];
                switch (Op.OperandType)
                {
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        MemberInfo? Member = null;
                        try
                        {
                            Member = Method.Module.ResolveMember(BitConverter.ToInt32(IL, i), TypeArguments, MethodArguments);
                        }
                        catch (ArgumentException)
                        {
                            // a generic context the scan does not reconstruct; not a library member
                        }
                        if (Member != null)
                        {
                            yield return Member;
                        }
                        i += 4;
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    case OperandType.InlineSwitch:
                        i += 4 + 4 * BitConverter.ToInt32(IL, i);
                        break;
                    default:
                        i += 4;
                        break;
                }
            }
        }
    }
}
