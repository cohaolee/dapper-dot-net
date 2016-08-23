using System;

namespace Dapper
{

    /// <summary>
    /// Additional state flags that control command behaviour
    /// </summary>
    [Flags]
    public enum CommandFlags
    {
        /// <summary>
        /// No additional flags
        /// </summary>
        None = 0,
        /// <summary>
        /// Should data be buffered before returning?
        /// 在返回结果前进行缓冲处理
        /// </summary>
        Buffered = 1,
        /// <summary>
        /// Can async queries be pipelined?
        /// 通过管道进行异步查询
        /// </summary>
        Pipelined = 2,
        /// <summary>
        /// Should the plan cache be bypassed?
        /// 是否绕过缓存
        /// </summary>
        NoCache = 4,
    }

}
