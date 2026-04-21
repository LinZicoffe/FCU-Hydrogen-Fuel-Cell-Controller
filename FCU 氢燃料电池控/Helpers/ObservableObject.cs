using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FCU_氢燃料电池控.Helpers
{
    /// <summary>
    /// 可观察对象基类 - 实现INotifyPropertyChanged接口
    /// 创建日期: 2026-02-03
    /// 用途: 为ViewModel提供属性变更通知功能
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <summary>
        /// 属性变更事件
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// </summary>
        /// <param name="propertyName">属性名称（自动获取）</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 设置属性值并触发变更通知
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="field">属性字段</param>
        /// <param name="value">新值</param>
        /// <param name="propertyName">属性名称（自动获取）</param>
        /// <returns>如果值发生变化返回true，否则返回false</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // 比较新旧值
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            // 设置新值
            field = value;

            // 触发属性变更通知
            OnPropertyChanged(propertyName);

            return true;
        }

        /// <summary>
        /// 设置属性值并在变更后执行回调
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="field">属性字段</param>
        /// <param name="value">新值</param>
        /// <param name="onChanged">变更后的回调</param>
        /// <param name="propertyName">属性名称（自动获取）</param>
        /// <returns>如果值发生变化返回true，否则返回false</returns>
        protected bool SetProperty<T>(ref T field, T value, Action onChanged, [CallerMemberName] string? propertyName = null)
        {
            if (SetProperty(ref field, value, propertyName))
            {
                onChanged?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 触发多个属性变更通知
        /// </summary>
        /// <param name="propertyNames">属性名称列表</param>
        protected void OnPropertiesChanged(params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                OnPropertyChanged(propertyName);
            }
        }
    }
}
