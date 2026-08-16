using System;

namespace FCU_氢燃料电池控.Protocol
{
    /// <summary>
    /// 协议常量定义类 - 统一管理所有协议相关的魔法数字
    /// 创建日期: 2026-02-03
    /// </summary>
    public static class ProtocolConstants
    {
        #region 帧结构常量

        /// <summary>
        /// 帧头第一字节
        /// </summary>
        public const byte FRAME_HEADER1 = 0xAA;

        /// <summary>
        /// 帧头第二字节
        /// </summary>
        public const byte FRAME_HEADER2 = 0x55;

        /// <summary>
        /// CRC初始值
        /// </summary>
        public const ushort CRC_INIT_VALUE = 0xFFFF;

        /// <summary>
        /// CRC多项式
        /// </summary>
        public const ushort CRC_POLYNOMIAL = 0x1021;

        /// <summary>
        /// 最小帧长度（帧头2 + 地址1 + 功能码1 + 长度1 + CRC2）
        /// </summary>
        public const int MIN_FRAME_LENGTH = 7;

        #endregion

        #region 功能码定义

        /// <summary>
        /// 功能码：读取序列号
        /// </summary>
        public const byte FUNC_READ_SERIAL_NUMBER = 0x01;

        /// <summary>
        /// 功能码：设置压力参数
        /// </summary>
        public const byte FUNC_SET_PRESSURE_PARAMS = 0x02;

        /// <summary>
        /// 功能码：设置阀门参数
        /// </summary>
        public const byte FUNC_SET_VALVE_PARAMS = 0x03;

        /// <summary>
        /// 功能码：设置拉载参数
        /// </summary>
        public const byte FUNC_SET_LOAD_PARAMS = 0x04;

        /// <summary>
        /// 功能码：设置DCDC参数
        /// </summary>
        public const byte FUNC_SET_DCDC_PARAMS = 0x05;

        /// <summary>
        /// 功能码：设置风扇及温度参数
        /// </summary>
        public const byte FUNC_SET_FAN_TEMP_PARAMS = 0x06;

        /// <summary>
        /// 功能码：读取电堆信息
        /// </summary>
        public const byte FUNC_READ_STACK_INFO = 0x07;

        /// <summary>
        /// 功能码：启动电堆
        /// </summary>
        public const byte FUNC_START_STACK = 0x08;

        /// <summary>
        /// 功能码：关闭电堆
        /// </summary>
        public const byte FUNC_STOP_STACK = 0x09;

        /// <summary>
        /// 功能码：读取配置参数
        /// </summary>
        public const byte FUNC_READ_CONFIG = 0x0A;

        /// <summary>
        /// 功能码：设置通用参数
        /// </summary>
        public const byte FUNC_SET_GENERAL_PARAMS = 0x0B;

        /// <summary>
        /// 功能码：修改设备地址
        /// </summary>
        public const byte FUNC_SET_DEVICE_ADDRESS = 0x0C;

        /// <summary>
        /// 功能码：设置BMS轮询使能 [SOC计划 §4.6] (数据域1B: 0=上位机接管BMS总线, 1=恢复固件轮询)
        /// 注意: 固件每次写Flash整页擦写, 仅允许会话级调用(连接/断开各一次), 禁止逐次握手
        /// </summary>
        public const byte FUNC_SET_BMS_POLL = 0x0D;

        /// <summary>
        /// 响应功能码：BMS轮询使能设置结果 (数据域1B结果码)
        /// </summary>
        public const byte RESP_BMS_POLL = 0x8D;

        /// <summary>
        /// 功能码：设置工作流程参数(purge/排水模式/拉载风扇) [SOC计划 §5.3] (数据域12B, 不含参数类型ID)
        /// </summary>
        public const byte FUNC_SET_FLOW_PARAMS = 0x0E;

        /// <summary>
        /// 响应功能码：工作流程参数设置结果 (数据域1B结果码)
        /// </summary>
        public const byte RESP_FLOW_PARAMS = 0x8E;

        /// <summary>
        /// 功能码：风扇手动运行控制（数据域2B：enable、duty）。
        /// 该指令只控制本次运行，不写入Flash；enable=0时duty被忽略。
        /// </summary>
        public const byte FUNC_SET_FAN_MANUAL = 0x0F;

        /// <summary>
        /// 响应功能码：风扇手动运行控制结果（数据域1B结果码）
        /// </summary>
        public const byte RESP_FAN_MANUAL = 0x8F;

        /// <summary>
        /// 功能码：设置 BMS/SOC 参数（数据域固定 5B，不含参数类型ID）。
        /// 写入 Flash 后需重启 FCU 生效。
        /// </summary>
        public const byte FUNC_SET_SOC_PARAMS = 0x10;

        /// <summary>
        /// 响应功能码：BMS/SOC 参数设置结果（数据域1B结果码）。
        /// </summary>
        public const byte RESP_SOC_PARAMS = 0x90;

        /// <summary>
        /// 响应功能码：读取配置参数
        /// </summary>
        public const byte RESP_READ_CONFIG = 0x8A;

        /// <summary>
        /// 功能码：报警信息上报
        /// </summary>
        public const byte FUNC_ALARM_REPORT = 0xF0;

        /// <summary>
        /// 功能码：状态变更上报
        /// </summary>
        public const byte FUNC_STATE_CHANGE_REPORT = 0xF1;

        /// <summary>
        /// 功能码：拉载状态上报
        /// </summary>
        public const byte FUNC_LOAD_STATUS_REPORT = 0xF2;

        #endregion

        #region 参数类型ID定义

        /// <summary>
        /// 参数类型：压力参数
        /// </summary>
        public const ushort PARAM_TYPE_PRESSURE = 0x0101;

        /// <summary>
        /// 参数类型：阀门参数
        /// </summary>
        public const ushort PARAM_TYPE_VALVE = 0x0201;

        /// <summary>
        /// 参数类型：DCDC参数
        /// </summary>
        public const ushort PARAM_TYPE_DCDC = 0x0301;

        /// <summary>
        /// 参数类型：风扇及温度参数
        /// </summary>
        public const ushort PARAM_TYPE_FAN_TEMP = 0x0401;

        /// <summary>
        /// 参数类型：拉载时序参数
        /// </summary>
        public const ushort PARAM_TYPE_LOAD_SEQUENCE = 0x0501;

        /// <summary>
        /// 参数类型：拉载控制参数
        /// </summary>
        public const ushort PARAM_TYPE_LOAD_CONTROL = 0x0502;

        /// <summary>
        /// 参数类型：拉载启动参数
        /// </summary>
        public const ushort PARAM_TYPE_LOAD_STARTUP = 0x0503;

        /// <summary>
        /// 参数类型：拉载手动控制
        /// </summary>
        public const ushort PARAM_TYPE_LOAD_MANUAL = 0x0504;

        /// <summary>
        /// 参数类型：工作流程参数 [SOC计划 §5.3]
        /// </summary>
        public const ushort PARAM_TYPE_FLOW = 0x0601;

        /// <summary>
        /// 参数类型：BMS/SOC 参数。
        /// </summary>
        public const ushort PARAM_TYPE_SOC = 0x0701;

        /// <summary>
        /// 参数类型：通用参数
        /// </summary>
        public const ushort PARAM_TYPE_GENERAL = 0x0901;

        #endregion

        #region 拉载手动控制指令码定义（参数类型0x0504）

        /// <summary>
        /// 拉载控制指令：启动拉载
        /// </summary>
        public const byte LOAD_CMD_START = 0x10;

        /// <summary>
        /// 拉载控制指令：暂停拉载
        /// </summary>
        public const byte LOAD_CMD_PAUSE = 0x11;

        /// <summary>
        /// 拉载控制指令：继续拉载
        /// </summary>
        public const byte LOAD_CMD_RESUME = 0x12;

        /// <summary>
        /// 拉载控制指令：停止拉载
        /// </summary>
        public const byte LOAD_CMD_STOP = 0x13;

        /// <summary>
        /// 拉载控制指令：跳转到时序
        /// </summary>
        public const byte LOAD_CMD_JUMP_SEQUENCE = 0x14;

        /// <summary>
        /// 拉载控制指令：设置目标电流
        /// </summary>
        public const byte LOAD_CMD_SET_CURRENT = 0x15;

        /// <summary>
        /// 拉载控制指令：单步执行
        /// </summary>
        public const byte LOAD_CMD_STEP = 0x16;

        /// <summary>
        /// 拉载控制指令：重置拉载
        /// </summary>
        public const byte LOAD_CMD_RESET = 0x17;

        /// <summary>
        /// 拉载状态上报数据长度（14字节）
        /// </summary>
        public const int LOAD_STATUS_DATA_LENGTH = 14;

        #endregion

        #region 数据长度常量

        /// <summary>
        /// 压力参数数据长度（字节）- 含参数类型ID共18字节
        /// </summary>
        public const int PRESSURE_PARAMS_DATA_LENGTH = 18;

        /// <summary>
        /// 阀门参数数据长度（字节）- 含参数类型ID共12字节
        /// </summary>
        public const int VALVE_PARAMS_DATA_LENGTH = 12;

        /// <summary>
        /// DCDC参数数据长度（字节）- 含参数类型ID共6字节
        /// </summary>
        public const int DCDC_PARAMS_DATA_LENGTH = 6;

        /// <summary>
        /// 风扇及温度参数数据长度（字节）- 含参数类型ID共13字节
        /// </summary>
        public const int FAN_TEMP_PARAMS_DATA_LENGTH = 13;

        /// <summary>
        /// 通用参数数据长度（字节）- 含参数类型ID共4字节
        /// </summary>
        public const int GENERAL_PARAMS_DATA_LENGTH = 4;

        /// <summary>
        /// BMS/SOC 设置参数数据长度（字节，不含参数类型ID）。
        /// </summary>
        public const int SOC_PARAMS_DATA_LENGTH = 5;

        /// <summary>
        /// 修改设备地址数据长度（字节）
        /// </summary>
        public const int SET_DEVICE_ADDRESS_DATA_LENGTH = 1;

        #endregion

        #region 默认值常量

        /// <summary>
        /// 默认设备地址
        /// </summary>
        public const byte DEFAULT_DEVICE_ADDRESS = 0x01;

        /// <summary>
        /// 默认波特率
        /// </summary>
        public const int DEFAULT_BAUD_RATE = 9600;

        /// <summary>
        /// UI显示默认值（当数据无效时）
        /// </summary>
        public const string UI_DEFAULT_VALUE = "--";

        #endregion

        #region 错误码定义

        /// <summary>
        /// 错误码：成功
        /// </summary>
        public const byte ERR_SUCCESS = 0x00;

        /// <summary>
        /// 错误码：功能不支持
        /// </summary>
        public const byte ERR_FUNC_NOT_SUPPORT = 0x01;

        /// <summary>
        /// 错误码：参数错误
        /// </summary>
        public const byte ERR_PARAM_ERROR = 0x02;

        /// <summary>
        /// 错误码：设备忙
        /// </summary>
        public const byte ERR_DEVICE_BUSY = 0x03;

        /// <summary>
        /// 错误码：CRC校验失败
        /// </summary>
        public const byte ERR_CRC_FAILED = 0x04;

        /// <summary>
        /// 错误码：数据长度错误
        /// </summary>
        public const byte ERR_DATA_LENGTH = 0x05;

        /// <summary>
        /// 错误码：地址无效
        /// </summary>
        public const byte ERR_ADDRESS_INVALID = 0x06;

        /// <summary>
        /// 错误码：执行失败
        /// </summary>
        public const byte ERR_EXECUTION_FAILED = 0x07;

        /// <summary>
        /// 错误码：设备未就绪
        /// </summary>
        public const byte ERR_NOT_READY = 0x08;

        /// <summary>
        /// 错误码：存储失败
        /// </summary>
        public const byte ERR_SAVE_FAILED = 0x09;

        /// <summary>
        /// 错误码：权限不足
        /// </summary>
        public const byte ERR_NO_PERMISSION = 0x0A;

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取功能码描述
        /// </summary>
        /// <param name="functionCode">功能码</param>
        /// <returns>功能码描述</returns>
        public static string GetFunctionCodeDescription(byte functionCode)
        {
            return functionCode switch
            {
                FUNC_READ_SERIAL_NUMBER => "读取序列号",
                FUNC_SET_PRESSURE_PARAMS => "设置压力参数",
                FUNC_SET_VALVE_PARAMS => "设置阀门参数",
                FUNC_SET_LOAD_PARAMS => "设置拉载参数",
                FUNC_SET_DCDC_PARAMS => "设置DCDC参数",
                FUNC_SET_FAN_TEMP_PARAMS => "设置风扇及温度参数",
                FUNC_READ_STACK_INFO => "读取电堆信息",
                FUNC_START_STACK => "启动电堆",
                FUNC_STOP_STACK => "关闭电堆",
                FUNC_READ_CONFIG => "读取配置参数",
                FUNC_SET_GENERAL_PARAMS => "设置通用参数",
                FUNC_SET_DEVICE_ADDRESS => "修改设备地址",
                FUNC_SET_BMS_POLL => "设置BMS轮询使能",
                RESP_BMS_POLL => "BMS轮询使能响应",
                FUNC_SET_FLOW_PARAMS => "设置工作流程参数",
                RESP_FLOW_PARAMS => "工作流程参数响应",
                FUNC_SET_FAN_MANUAL => "风扇手动运行控制",
                RESP_FAN_MANUAL => "风扇手动运行控制响应",
                FUNC_SET_SOC_PARAMS => "设置BMS/SOC参数",
                RESP_SOC_PARAMS => "BMS/SOC参数设置响应",
                FUNC_ALARM_REPORT => "报警信息上报",
                FUNC_STATE_CHANGE_REPORT => "状态变更上报",
                FUNC_LOAD_STATUS_REPORT => "拉载状态上报",
                _ => $"未知功能码(0x{functionCode:X2})"
            };
        }

        /// <summary>
        /// 获取错误码描述
        /// </summary>
        /// <param name="errorCode">错误码</param>
        /// <returns>错误码描述</returns>
        public static string GetErrorCodeDescription(byte errorCode)
        {
            return errorCode switch
            {
                ERR_SUCCESS => "成功",
                ERR_FUNC_NOT_SUPPORT => "功能不支持",
                ERR_PARAM_ERROR => "参数错误",
                ERR_DEVICE_BUSY => "设备忙",
                ERR_CRC_FAILED => "CRC校验失败",
                ERR_DATA_LENGTH => "数据长度错误",
                ERR_ADDRESS_INVALID => "地址无效",
                ERR_EXECUTION_FAILED => "执行失败",
                ERR_NOT_READY => "设备未就绪",
                ERR_SAVE_FAILED => "存储失败",
                ERR_NO_PERMISSION => "权限不足",
                _ => $"未知错误(0x{errorCode:X2})"
            };
        }

        /// <summary>
        /// 获取拉载状态描述
        /// </summary>
        /// <param name="loadState">拉载状态码</param>
        /// <returns>拉载状态描述</returns>
        public static string GetLoadStateDescription(byte loadState)
        {
            return loadState switch
            {
                0x00 => "未开始",
                0x01 => "进行中",
                0x02 => "完成",
                0x03 => "暂停",
                0x04 => "异常停止",
                0x05 => "锁定成功",
                0x06 => "锁定失败",
                _ => $"未知状态(0x{loadState:X2})"
            };
        }

        #endregion
    }
}
