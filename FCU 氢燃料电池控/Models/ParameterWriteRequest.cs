namespace FCU_氢燃料电池控.Models
{
    /// <summary>一次写入的目标快照；数组由协议编码器新建，不引用界面输入。</summary>
    public sealed record ParameterWriteRequest(byte Address, ushort ParameterId, string Name, byte ResponseFunction,
        byte[] WriteFrame, byte[] ReadFrame, byte[] ExpectedBody);
}
