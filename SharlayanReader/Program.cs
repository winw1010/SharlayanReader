#region Using
using Newtonsoft.Json;
using Sharlayan;
using Sharlayan.Core;
using Sharlayan.Extensions;
using Sharlayan.Models;
using Sharlayan.Models.ReadResults;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
#endregion

#region Variable
bool isRunning = false;

int _previousArrayIndex = 0;
int _previousOffset = 0;

string lastChatLogMessage = "";
string lastDialogText = "";
string lastCutsceneText = "";

List<string> dialogHistory = new List<string>();
#endregion

#region Main Process
MainProcess();

void MainProcess()
{
    try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

    while (true)
    {
        try
        {
            MemoryHandler memoryHandler = CreateMemoryHandler();
            RunReader(memoryHandler);
        }
        catch (Exception ex)
        {
            WriteData("CONSOLE", "FFFF", "", ex.Message);
        }
        TaskDelay(1000);
    }
}

MemoryHandler CreateMemoryHandler()
{
    // Get process
    Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
    if (processes.Length <= 0) { throw new Exception("Waiting..."); }

    // Create configuration
    SharlayanConfiguration configuration = new SharlayanConfiguration
    {
        ProcessModel = new ProcessModel
        {
            Process = processes.FirstOrDefault(),
        },
    };

    // Create memoryHandler
    MemoryHandler memoryHandler = new MemoryHandler(configuration);
    memoryHandler.Scanner.Locations.Clear();

    // Set signatures
    string signaturesText = File.ReadAllText("signatures.json");
    var signatures = JsonConvert.DeserializeObject<List<Signature>>(signaturesText);
    if (signatures != null)
    {
        memoryHandler.Scanner.LoadOffsets(signatures.ToArray());
    }

    return memoryHandler;
}

#endregion

#region Chat Log Functions
void ReadChatLog(MemoryHandler memoryHandler)
{
    try
    {
        ChatLogResult readResult = memoryHandler.Reader.GetChatLog(_previousArrayIndex, _previousOffset);
        List<ChatLogItem> chatLogEntries = readResult.ChatLogItems.ToList();

        _previousArrayIndex = readResult.PreviousArrayIndex;
        _previousOffset = readResult.PreviousOffset;

        if (chatLogEntries.Count > 0)
        {
            for (int i = 0; i < chatLogEntries.Count; i++)
            {
                ChatLogItem chatLogItem = chatLogEntries[i];

                if (chatLogItem.Message != lastChatLogMessage)
                {
                    lastChatLogMessage = chatLogItem.Message;

                    string logName = getLogName(chatLogItem);
                    string logText = chatLogItem.Message;

                    /*
                    if (logName.Length == 0 && systemCode.IndexOf(chatLogItem.Code) < 0)
                    {
                        string[] splitedMessage = logText.Split(':');
                        if (splitedMessage.Length > 1 && splitedMessage[0].Length > 0)
                        {
                            logName = splitedMessage[0];
                            logText = logText.Replace(logName + ":", "");
                        }
                    }
                    */

                    if (chatLogItem.Code != "003D" || isNotRepeated(chatLogItem.Code, logText))
                    {
                        WriteData("CHAT_LOG", chatLogItem.Code, logName, logText);
                    }
                }
            }
        }
    }
    catch (Exception)
    {
    }

    return;
}

string getLogName(ChatLogItem chatLogItem)
{
    string logName = "";

    try
    {
        if (chatLogItem.PlayerName != null)
        {
            logName = chatLogItem.PlayerName;
        }
    }
    catch (Exception)
    {
    }

    return logName;
}

void addHistory(string text)
{
    text = ChatCleaner.ProcessFullLine("003D", text).Replace("\r", "");
    dialogHistory.Add(text);
    if (dialogHistory.Count > 20)
    {
        int newCount = dialogHistory.Count / 2;
        dialogHistory.RemoveRange(0, newCount);
    }
}

bool isNotRepeated(string code, string text)
{
    text = ChatCleaner.ProcessFullLine(code, text).Replace("\r", "");
    List<string> history = dialogHistory.ToList();

    if (history.Count > 0)
    {
        int lastIndex = history.LastIndexOf(text);
        return lastIndex < 0 || lastIndex < history.Count - 2;
    }
    else
    {
        return true;
    }
}
#endregion

#region Dialog Functions
void ReadDialog(MemoryHandler memoryHandler)
{
    try
    {
        /*
        string[] result = GetDialogPanel(memoryHandler);

        if (result.Length > 0 && result[1] != lastDialogString)
        {
            lastDialogString = result[1];
            addHistory(result[1]);
            PassData("DIALOG", "003D", result[0], result[1]);
        }
        */

        string dialogName = GetByteString(memoryHandler, "PANEL_NAME", 128);
        string dialogText = GetByteString(memoryHandler, "PANEL_TEXT", 512);

        if (dialogName.Length > 0 && dialogText.Length > 0 && dialogText != lastDialogText)
        {
            lastDialogText = dialogText;
            addHistory(dialogText);
            WriteData("DIALOG", "003D", dialogName, dialogText);
        }
    }
    catch (Exception)
    {
    }

    return;
}

/*
string[] GetDialogPanel(MemoryHandler memoryHandler)
{
    string[] result = new string[] { };

    var dialogPanelNamePointer = (IntPtr)memoryHandler.Scanner.Locations["PANEL_NAME"];
    var dialogPanelNameLengthPointer = IntPtr.Subtract(dialogPanelNamePointer, 18);

    var dialogPanelTextPointer = (IntPtr)memoryHandler.Scanner.Locations["PANEL_TEXT"];
    var dialogPanelTextPointer2 = new IntPtr((long)memoryHandler.GetUInt64(dialogPanelTextPointer));

    var dialogPanelTextLegthPointer = IntPtr.Add(dialogPanelTextPointer, 16);

    int nameLength = (int)memoryHandler.GetInt64(dialogPanelNameLengthPointer);
    int textLength = (int)memoryHandler.GetInt64(dialogPanelTextLegthPointer);

    if (textLength > 1 && nameLength > 1)
    {
        if (textLength > 512)
            textLength = 512;

        if (nameLength > 128)
            nameLength = 128;

        byte[] npcNameBytes = memoryHandler.GetByteArray(dialogPanelNamePointer, nameLength);
        byte[] textBytes = memoryHandler.GetByteArray(dialogPanelTextPointer2, textLength);

        nameLength = GetRealTextLength(ref npcNameBytes);
        textLength = GetRealTextLength(ref textBytes);

        //Int32 unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        //byte[] unixTimestampBytes = BitConverter.GetBytes(unixTimestamp).ToArray();

        result = new string[] { ByteToString(npcNameBytes), ByteToString(textBytes) };
    }

    return result;
}

int GetRealTextLength(ref byte[] byteArray)
{
    int textEnd = 0;

    for (int i = 0; i < byteArray.Length; i++)
    {
        if (byteArray[i] == 0)
        {
            textEnd = i + 1;
            break;
        }
    }

    if (textEnd != 0 && textEnd <= byteArray.Length)
    {
        byte[] newArr = new byte[textEnd];
        Array.Copy(byteArray, newArr, newArr.Length);
        byteArray = newArr;
    }
    else
    {
        byteArray = new byte[0];
    }

    return textEnd;
}
*/
#endregion

#region Cutscene Functions
void ReadCutscene(MemoryHandler memoryHandler)
{
    try
    {
        var cutsceneDetectorPointer = (IntPtr)memoryHandler.Scanner.Locations["CUTSCENE_DETECTOR"];
        int isCutscene = (int)memoryHandler.GetInt64(cutsceneDetectorPointer);

        if (isCutscene == 1) return;

        string cutsceneText = GetByteString(memoryHandler, "CUTSCENE_TEXT", 256);

        if (cutsceneText.Length > 0 && cutsceneText != lastCutsceneText)
        {
            lastCutsceneText = cutsceneText;
            WriteData("CUTSCENE", "003D", "", cutsceneText, 1000);
        }
    }
    catch (Exception)
    {
    }

    return;
}
#endregion

#region Byte Functions
/*
bool CompareArray(byte[] byteArray1, byte[] byteArray2)
{
    if (byteArray1.Length != byteArray2.Length) { return false; }

    for (int i = 0; i < byteArray1.Length; i++)
    {
        if (byteArray1[i] != byteArray2[i]) { return false; }
    }

    return true;
}
*/

string GetByteString(MemoryHandler memoryHandler, string key, int length)
{
    string byteString = "";

    try
    {
        byte[] byteArray = memoryHandler.GetByteArray(memoryHandler.Scanner.Locations[key], length);
        byteString = ByteArrayToString(GetRealByteArray(byteArray));
    }
    catch (Exception)
    {
    }

    return byteString;
}

byte[] GetRealByteArray(byte[] byteArray)
{
    List<byte> byteList = new List<byte>();
    int nullIndex = byteArray.ToList().IndexOf(0x00);

    for (int i = 0; i < nullIndex; i++)
    {
        byteList.Add(byteArray[i]);
    }

    return byteList.ToArray();
}


string ByteArrayToString(byte[] byteArray)
{
    return Encoding.UTF8.GetString(byteArray);
}
#endregion

#region System Functions
void RunReader(MemoryHandler memoryHandler)
{
    WriteData("CONSOLE", "FFFF", "", "Start reading...");

    isRunning = true;
    Task.Run(AliveCheck);

    while (isRunning)
    {
        if (!memoryHandler.Scanner.IsScanning)
        {
            ReadChatLog(memoryHandler);
            ReadDialog(memoryHandler);
            ReadCutscene(memoryHandler);
        }

        TaskDelay();
    }

    WriteData("CONSOLE", "FFFF", "", "Stop reading...");
}

async Task AliveCheck()
{
    while (true)
    {
        Process[] processes = Process.GetProcessesByName("ffxiv_dx11");
        if (processes.Length > 0)
        {
            isRunning = true;
            await Task.Delay(1000);
        }
        else
        {
            isRunning = false;
            break;
        }
    }
}

void TaskDelay(int delayTIme = 50)
{
    try
    {
        Task.Delay(delayTIme).Wait();
    }
    catch (Exception)
    {
    }
}

async void WriteData(string type, string code, string name, string text, int sleepTime = 0)
{
    await Task.Delay(sleepTime);

    string dataString = JsonConvert.SerializeObject(new
    {
        type,
        code,
        name = ChatCleaner.ProcessFullLine(code, name),
        text = ChatCleaner.ProcessFullLine(code, text)
    });

    Console.Write(dataString + "\r\n");

    return;
}
#endregion

#region Class Definition
class ChatCleaner
{
    private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.ExplicitCapture;

    //private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly Regex PlayerChatCodesRegex = new Regex(@"^00(0[A-F]|1[0-9A-F])$", DefaultOptions);

    private static readonly Regex PlayerRegEx = new Regex(@"(?<full>\[[A-Z0-9]{10}(?<first>[A-Z0-9]{3,})20(?<last>[A-Z0-9]{3,})\](?<short>[\w']+\.? [\w']+\.?)\[[A-Z0-9]{12}\])", DefaultOptions);

    private static readonly Regex ArrowRegex = new Regex(@"\uE06F", RegexOptions.Compiled);

    private static readonly Regex HQRegex = new Regex(@"\uE03C", RegexOptions.Compiled);

    //private static readonly Regex NewLineRegex = new Regex(@"[\r\n]+", RegexOptions.Compiled);

    private static readonly Regex NoPrintingCharactersRegex = new Regex(@"[\x00-\x0C\x0E-\x1F\x7F]+", RegexOptions.Compiled);

    private static readonly Regex SpecialPurposeUnicodeRegex = new Regex(@"[\uE000-\uF8FF]", RegexOptions.Compiled);

    private static readonly Regex SpecialReplacementRegex = new Regex(@"[\uFFFD]", RegexOptions.Compiled);

    public static string ProcessFullLine(string code, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var line = HttpUtility.HtmlDecode(Encoding.UTF8.GetString(bytes.ToArray())).Replace("  ", " ");
        try
        {
            List<byte> autoTranslateList = new List<byte>(bytes.Length);

            List<byte> newList = new List<byte>();

            for (var x = 0; x < bytes.Count(); x++)
            {
                switch (bytes[x])
                {
                    case 2:
                        // special in-game replacements/wrappers
                        // 2 46 5 7 242 2 210 3
                        // 2 29 1 3
                        // remove them
                        var length = bytes[x + 2];
                        var limit = length - 1;
                        if (length > 1)
                        {
                            x = x + 3;

                            ///////////////////////////
                            autoTranslateList.Add(Convert.ToByte('['));
                            byte[] translated = new byte[limit];
                            Buffer.BlockCopy(bytes, x, translated, 0, limit);
                            foreach (var b in translated)
                            {
                                autoTranslateList.AddRange(Encoding.UTF8.GetBytes(b.ToString("X2")));
                            }

                            autoTranslateList.Add(Convert.ToByte(']'));

                            var bCheckStr = Encoding.UTF8.GetString(autoTranslateList.ToArray());

                            if (bCheckStr != null && bCheckStr.Length > 0)
                            {
                                if (bCheckStr.Equals("[59]"))
                                {
                                    newList.Add(0x40);
                                }

                                /*
                                if (Utilities.AutoTranslate.EnDict.TryGetValue(bCheckStr.Replace("[0", "[").ToLower(), out var AutoTranslateVal))
                                {
                                    newList.AddRange(Encoding.UTF8.GetBytes(AutoTranslateVal));
                                }
                                */
                            }
                            autoTranslateList.Clear();
                            ///////////////////////////

                            x += limit;
                        }
                        else
                        {
                            x = x + 4;
                            newList.Add(32);
                            newList.Add(bytes[x]);
                        }

                        break;

                    // unit separator
                    case 31:
                        // TODO: this breaks in some areas like NOVICE chat
                        // if (PlayerChatCodesRegex.IsMatch(code)) {
                        //     newList.Add(58);
                        // }
                        // else {
                        //     newList.Add(31);
                        // }
                        newList.Add(58);
                        if (PlayerChatCodesRegex.IsMatch(code))
                        {
                            newList.Add(32);
                        }
                        break;
                    default:
                        newList.Add(bytes[x]);
                        break;
                }
            }

            var cleaned = HttpUtility.HtmlDecode(Encoding.UTF8.GetString(newList.ToArray())).Replace("  ", " ");

            newList.Clear();

            // replace right arrow in chat (parsing)
            cleaned = ArrowRegex.Replace(cleaned, "⇒");
            // replace HQ symbol
            cleaned = HQRegex.Replace(cleaned, "[HQ]");
            // replace all Extended special purpose unicode with empty string
            cleaned = SpecialPurposeUnicodeRegex.Replace(cleaned, string.Empty);
            // cleanup special replacement character bytes: 239 191 189
            cleaned = SpecialReplacementRegex.Replace(cleaned, string.Empty);
            // remove new lines
            //cleaned = NewLineRegex.Replace(cleaned, string.Empty);
            // remove characters 0-31
            cleaned = NoPrintingCharactersRegex.Replace(cleaned, string.Empty);

            line = cleaned;
        }
        catch (Exception)
        {
            //MemoryHandler.Instance.RaiseException(Logger, ex, true);
            //Console.WriteLine(ex.Message);
        }

        return ProcessName(line);
    }

    private static string ProcessName(string cleaned)
    {
        var line = cleaned;
        try
        {
            // cleanup name if using other settings
            Match playerMatch = PlayerRegEx.Match(line);
            if (playerMatch.Success)
            {
                var fullName = playerMatch.Groups[1].Value;
                var firstName = playerMatch.Groups[2].Value.FromHex();
                var lastName = playerMatch.Groups[3].Value.FromHex();
                var player = $"{firstName} {lastName}";

                // remove double placement
                cleaned = line.Replace($"{fullName}:{fullName}", "•name•");

                // remove single placement
                cleaned = cleaned.Replace(fullName, "•name•");
                switch (Regex.IsMatch(cleaned, @"^([Vv]ous|[Dd]u|[Yy]ou)"))
                {
                    case true:
                        cleaned = cleaned.Substring(1).Replace("•name•", string.Empty);
                        break;
                    case false:
                        cleaned = cleaned.Replace("•name•", player);
                        break;
                }
            }

            //cleaned = Regex.Replace(cleaned, @"[\r\n]+", string.Empty);
            cleaned = Regex.Replace(cleaned, @"[\x00-\x0C\x0E-\x1F\x7F]+", string.Empty);
            line = cleaned;
        }
        catch (Exception)
        {
            //MemoryHandler.Instance.RaiseException(Logger, ex, true);
            //Console.WriteLine(ex.Message);
        }

        return line;
    }
}
#endregion