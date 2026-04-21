using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace FCU_氢燃料电池控.Managers
{
    /// <summary>
    /// 串口管理器 - 负责串口的打开、关闭、数据收发等操作
    /// 创建日期: 2026-02-03
    /// 用途: 将串口相关操作从MainWindow中分离，降低耦合度
    /// </summary>
    public class SerialPortManager : IDisposable
    {
        private SerialPort? _serialPort;
        private bool _isDisposed;

        #region 事件定义

        /// <summary>
        /// 串口数据接收事件
        /// </summary>
        public event EventHandler<SerialDataReceivedEventArgs>? DataReceived;

        /// <summary>
        /// 串口连接状态改变事件
        /// </summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        /// <summary>
        /// 串口错误事件
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        #endregion

        #region 属性

        /// <summary>
        /// 串口是否已打开
        /// </summary>
        public bool IsOpen => _serialPort?.IsOpen == true;

        /// <summary>
        /// 当前串口名称
        /// </summary>
        public string? PortName => _serialPort?.PortName;

        /// <summary>
        /// 当前波特率
        /// </summary>
        public int BaudRate => _serialPort?.BaudRate ?? 0;

        #endregion

        #region 串口操作

        /// <summary>
        /// 获取可用串口列表
        /// </summary>
        /// <returns>串口名称列表</returns>
        public static List<string> GetAvailablePorts()
        {
            try
            {
                return SerialPort.GetPortNames().OrderBy(p => p).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// 打开串口
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="dataBits">数据位</param>
        /// <param name="stopBits">停止位</param>
        /// <param name="parity">校验位</param>
        /// <returns>是否成功打开</returns>
        public bool Open(string portName, int baudRate, int dataBits = 8, 
            StopBits stopBits = StopBits.One, Parity parity = Parity.None)
        {
            try
            {
                // 如果已经打开，先关闭
                if (IsOpen)
                {
                    Close();
                }

                // 创建串口对象
                _serialPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = dataBits,
                    StopBits = stopBits,
                    Parity = parity
                };

                // 注册数据接收事件
                _serialPort.DataReceived += OnSerialPortDataReceived;

                // 打开串口
                _serialPort.Open();

                // 触发连接状态改变事件
                ConnectionStateChanged?.Invoke(this, true);

                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"打开串口失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public void Close()
        {
            try
            {
                if (_serialPort != null)
                {
                    // 取消数据接收事件
                    _serialPort.DataReceived -= OnSerialPortDataReceived;

                    // 关闭串口
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }

                    // 释放串口对象
                    _serialPort.Dispose();
                    _serialPort = null;

                    // 触发连接状态改变事件
                    ConnectionStateChanged?.Invoke(this, false);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"关闭串口失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <returns>是否发送成功</returns>
        public bool SendData(byte[] data)
        {
            try
            {
                if (!IsOpen)
                {
                    ErrorOccurred?.Invoke(this, "串口未打开");
                    return false;
                }

                if (data == null || data.Length == 0)
                {
                    ErrorOccurred?.Invoke(this, "发送数据为空");
                    return false;
                }

                _serialPort!.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"发送数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清空接收缓冲区
        /// </summary>
        public void ClearReceiveBuffer()
        {
            try
            {
                if (IsOpen)
                {
                    _serialPort!.DiscardInBuffer();
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"清空接收缓冲区失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清空发送缓冲区
        /// </summary>
        public void ClearSendBuffer()
        {
            try
            {
                if (IsOpen)
                {
                    _serialPort!.DiscardOutBuffer();
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"清空发送缓冲区失败: {ex.Message}");
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 串口数据接收事件处理
        /// </summary>
        private void OnSerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // 触发数据接收事件，让外部处理
                DataReceived?.Invoke(sender, e);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"数据接收处理失败: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    Close();
                }
                _isDisposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~SerialPortManager()
        {
            Dispose(false);
        }

        #endregion
    }
}
