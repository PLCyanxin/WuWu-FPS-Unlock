using System.Globalization;

namespace WuWaFpsUnlock.Services;

public enum FpsCoreState { WaitingRenderer, SearchingMarker, SearchingCandidate, WaitingCommand, Maintaining, Suspended, Stopped }
public enum FpsCoreReason { None, MarkerNotFound, SecondMarkerUnavailable, CandidateUnreadable, CandidateTimeout, TargetUnreadable, TargetValueInvalid, TargetWriteFailed, TargetMappingChanged, RendererTimeout, StopRequested, AnchorChanged, InternalFailure }

// Describes the native candidate maintenance loop, never measured game FPS.
public sealed record FpsCoreStatus(int Pid, FpsCoreState State, FpsCoreReason Reason, int Target, int Applied, ulong Repairs)
{
    public string ToDisplayText()
    {
        string state = State switch
        {
            FpsCoreState.WaitingRenderer => "正在等待游戏就绪",
            FpsCoreState.SearchingMarker or FpsCoreState.SearchingCandidate => "帧率解锁正在准备",
            FpsCoreState.WaitingCommand => "正在等待帧率设置",
            FpsCoreState.Maintaining when Applied == Target && Target > 0 => $"帧率限制维持中（目标 {Target} FPS）",
            FpsCoreState.Maintaining => $"正在应用帧率限制（目标 {Target} FPS）",
            FpsCoreState.Suspended => "帧率限制暂不可用",
            _ => "帧率限制已停止"
        };
        string reason = Reason switch
        {
            FpsCoreReason.None => "",
            FpsCoreReason.MarkerNotFound or FpsCoreReason.SecondMarkerUnavailable or FpsCoreReason.CandidateUnreadable or FpsCoreReason.CandidateTimeout or FpsCoreReason.AnchorChanged => "：未能定位游戏帧率设置",
            FpsCoreReason.TargetUnreadable or FpsCoreReason.TargetValueInvalid or FpsCoreReason.TargetWriteFailed or FpsCoreReason.TargetMappingChanged => "：游戏帧率设置已变化或不可访问",
            FpsCoreReason.RendererTimeout => "：等待游戏就绪超时",
            FpsCoreReason.StopRequested => "：已收到停止请求",
            _ => "：核心运行错误"
        };
        return state + reason + "；实际帧率以游戏为准";
    }
    public string ToDiagnosticText() => $"PID={Pid}；state={(int)State}/{State}；reason={(int)Reason}/{Reason}；target={Target}；applied={Applied}；repairs={Repairs}";

    internal static FpsCoreStatus Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 20 or > 128 || bytes[^1] != (byte)'\n') throw new InvalidDataException("FPS 状态回执长度或结尾无效。");
        foreach (byte value in bytes)
            if (value != (byte)'\n' && value is not (>= (byte)'0' and <= (byte)'9') && value != (byte)',' && value != (byte)'W' && value != (byte)'U' && value != (byte)'A' && value != (byte)'-' && value != (byte)'F' && value != (byte)'P' && value != (byte)'S' && value != (byte)'/')
                throw new InvalidDataException("FPS 状态回执包含无效字符。");
        string line = System.Text.Encoding.ASCII.GetString(bytes[..^1]);
        string[] fields = line.Split(',');
        if (fields.Length != 7 || fields[0] != "WUWA-FPS/1") throw new InvalidDataException("FPS 状态协议版本或字段数量无效。");
        static int Number(string field, int min, int max)
        {
            if (field.Length == 0 || field.Length > 10 || field.Any(c => c is < '0' or > '9') ||
                !int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
                throw new InvalidDataException("FPS 状态字段超出允许范围。");
            return value;
        }
        int pid = Number(fields[1], 1, int.MaxValue);
        var state = (FpsCoreState)Number(fields[2], 0, 6);
        var reason = (FpsCoreReason)Number(fields[3], 0, 12);
        int target = Number(fields[4], 0, 420);
        if (target is > 0 and < 30) throw new InvalidDataException("FPS 状态目标值无效。");
        int applied = Number(fields[5], 0, 420);
        if (applied is > 0 and < 30) throw new InvalidDataException("FPS 状态已维持值无效。");
        if (fields[6].Length == 0 || fields[6].Length > 20 || fields[6].Any(c => c is < '0' or > '9') ||
            !ulong.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out ulong repairs))
            throw new InvalidDataException("FPS 状态修复次数无效。");
        return new(pid, state, reason, target, applied, repairs);
    }
}
