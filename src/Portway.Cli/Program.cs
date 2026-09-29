using Portway.Core.Automation;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("portway-cli --script file.txt | /script=file.txt | /command \"open ...\" \"ls\" \"exit\"\n명령: open close session option cd lcd pwd lpwd ls lls get put mkdir rm rmdir mv cp ln chmod checksum call synchronize keepuptodate echo exit\nSSH 연결에는 정확한 SHA256 -hostkey 지문이 필요합니다. 암호는 -passwordenv=VARIABLE로 전달하세요. 지원하지 않는 옵션은 오류로 처리합니다. doc/Portway.Document/docs/AUTOMATION.md를 참고하세요.");
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};
try
{
    string[] lines;
    if (args.Length == 2 && args[0] == "--script")
        lines = await File.ReadAllLinesAsync(args[1], cancellation.Token);
    else if (args.Length == 1 && args[0].StartsWith("/script=", StringComparison.OrdinalIgnoreCase))
        lines = await File.ReadAllLinesAsync(args[0][8..], cancellation.Token);
    else if (args[0] is "/command" or "--command")
        lines = args[1..];
    else
        throw new ArgumentException("지원하지 않는 명령줄입니다. --help로 사용법을 확인하세요.");
    await using var engine = new ScriptEngine(Console.Out);
    return await engine.Run(lines, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("작업이 취소되었습니다.");
    return 130;
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
