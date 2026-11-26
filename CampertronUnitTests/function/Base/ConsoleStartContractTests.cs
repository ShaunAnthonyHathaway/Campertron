using System.Reflection;
using System.Reflection.Emit;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.WebAccess;

namespace CampertronUnitTests.function.Base
{
    // The console's start as it was before v0.4.0's phase 6 (D115): the contracts that the interactive console, added
    // beside it, must leave unchanged. The no-argument menu keeps its four options in their order and words (ER-21: the
    // terminal gets the same bytes); its refusals keep their words; the arguments that phase 6 gives the console are not
    // web mode's, and a web-mode command line that holds one is refused by web mode's own words, as before. No terminal,
    // no file, no network.
    public class ConsoleStartContractTests
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        [Test]
        public void TheNoArgumentMenu_KeepsItsFourOptions_InTheirOrderAndWords()
        {
            MethodBase WriteMenu = typeof(Menu).GetMethod(nameof(Menu.WriteMenu), All)!;

            Assert.That(StringLiterals(WriteMenu), Is.EqualTo(new[] { "Search", "Refresh Ridb Data (slow)", "View Config", "Exit" }),
                "the menu's options are its bytes (ER-21, D115): the interactive console has its own menu");
        }

        [Test]
        public void TheNoArgumentMenu_KeepsItsRefusals_WordForWord()
        {
            Assert.That(Menu.StartProblem(true, true), Is.EqualTo("Unsupported output type in container. The container sends results by email or ntfy only: set outputTo: Email or Ntfy in /config/General.yaml."));
            Assert.That(Menu.StartProblem(false, false), Is.EqualTo("the Campertron menu needs an interactive terminal. For unattended use, set outputTo: Email or Ntfy in General.yaml."));
            Assert.That(Menu.StartProblem(false, true), Is.Null);
        }

        [TestCase("--interactive")]
        [TestCase("--help")]
        [TestCase("-h")]
        [TestCase("/?")]
        [TestCase("--Interactive")]
        [TestCase("anything")]
        public void AnArgumentThatIsNotWebModes_LeavesTheStartToTheConsole(string Argument)
        {
            Assert.That(WebArguments.IsRequested(new[] { Argument }), Is.False);
            Assert.That(WebArguments.Parse(new[] { Argument }).Command, Is.EqualTo(WebCommand.None));
            Assert.That(WebArguments.Parse(new[] { Argument }).Error, Is.Null);
        }

        [TestCase("--web", "--interactive")]
        [TestCase("--interactive", "--web")]
        [TestCase("--web", "--help")]
        [TestCase("--healthcheck", "--interactive")]
        [TestCase("--interactive", "--port", "8790")]
        public void AWebModeCommandLine_WithAnotherArgument_IsRefusedByWebModesOwnWords(params string[] Arguments)
        {
            WebArguments Parsed = WebArguments.Parse(Arguments);

            Assert.That(WebArguments.IsRequested(Arguments), Is.True, "a line with one of web mode's arguments is web mode's");
            Assert.That(Parsed.Command, Is.EqualTo(WebCommand.None));
            Assert.That(Parsed.Error, Is.EqualTo("an argument was given that web mode does not know: it knows --web, --healthcheck, --listen and --port."));
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(F => (OpCode)F.GetValue(null)!).ToDictionary(O => O.Value);

        // the string literals a method body loads (ldstr), in the order of its IL
        internal static List<string> StringLiterals(MethodBase Method)
        {
            var Literals = new List<string>();
            byte[]? IL = Method.GetMethodBody()?.GetILAsByteArray();
            if (IL == null)
            {
                return Literals;
            }
            int i = 0;
            while (i < IL.Length)
            {
                short Value = IL[i] == 0xFE ? (short)(0xFE00 | IL[i + 1]) : IL[i];
                i += IL[i] == 0xFE ? 2 : 1;
                OpCode Op = OpCodesByValue[Value];
                switch (Op.OperandType)
                {
                    case OperandType.InlineString:
                        Literals.Add(Method.Module.ResolveString(BitConverter.ToInt32(IL, i)));
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
            return Literals;
        }
    }
}
