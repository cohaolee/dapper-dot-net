using System;
using System.Data;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace Dapper
{
    /// <summary>
    /// Represents the key aspects of a sql operation
    /// 代表一个sql操作的关键方面
    /// </summary>
    public struct CommandDefinition
    {
        internal static CommandDefinition ForCallback(object parameters)
        {
            if (parameters is DynamicParameters)
            {
                return new CommandDefinition(parameters);
            }
            else
            {
                return default(CommandDefinition);
            }
        }

        internal void OnCompleted()
        {
            (Parameters as SqlMapper.IParameterCallbacks)?.OnCompleted();
        }

        /// <summary>
        /// The command (sql or a stored-procedure name) to execute
        /// 用于执行的命令（sql或存储过程名称）
        /// </summary>
        public string CommandText { get; }

        /// <summary>
        /// The parameters associated with the command
        /// 命令关联的参数
        /// </summary>
        public object Parameters { get; }

        /// <summary>
        /// The active transaction for the command
        /// 命令的激活事务
        /// </summary>
        public IDbTransaction Transaction { get; }

        /// <summary>
        /// The effective timeout for the command
        /// 命令的执行超时时间
        /// </summary>
        public int? CommandTimeout { get; }

        /// <summary>
        /// The type of command that the command-text represents
        /// 命令的类型（文本sql，存储过程，）
        /// </summary>
        public CommandType? CommandType { get; }

        /// <summary>
        /// Should data be buffered before returning?
        /// 配置在返回数据前是否需要缓冲
        /// </summary>
        public bool Buffered => (Flags & CommandFlags.Buffered) != 0;

        /// <summary>
        /// Should the plan for this query be cached?
        /// 配置 是否需要缓存本次查询
        /// </summary>
        internal bool AddToCache => (Flags & CommandFlags.NoCache) == 0;

        /// <summary>
        /// Additional state flags against this command
        /// 针对命令添加额外的状态标识
        /// </summary>
        public CommandFlags Flags { get; }

        /// <summary>
        /// Can async queries be pipelined?
        /// 是否可以管线式异步查询
        /// </summary>
        public bool Pipelined => (Flags & CommandFlags.Pipelined) != 0;

        /// <summary>
        /// Initialize the command definition
        /// 初始化
        /// </summary>
        public CommandDefinition(string commandText, object parameters = null, IDbTransaction transaction = null, int? commandTimeout = null,
                                 CommandType? commandType = null, CommandFlags flags = CommandFlags.Buffered
#if ASYNC
                                 , CancellationToken cancellationToken = default(CancellationToken)
#endif
            )
        {
            CommandText = commandText;
            Parameters = parameters;
            Transaction = transaction;
            CommandTimeout = commandTimeout;
            CommandType = commandType;
            Flags = flags;
#if ASYNC
            CancellationToken = cancellationToken;
#endif
        }

        private CommandDefinition(object parameters) : this()
        {
            Parameters = parameters;
        }

#if ASYNC

        /// <summary>
        /// For asynchronous operations, the cancellation-token
        /// </summary>
        public CancellationToken CancellationToken { get; }
#endif

        /// <summary>
        /// 将CommandDefinition的相关参数设置给IDbCommand
        /// </summary>
        /// <param name="cnn"></param>
        /// <param name="paramReader"></param>
        /// <returns></returns>
        internal IDbCommand SetupCommand(IDbConnection cnn, Action<IDbCommand, object> paramReader)
        {
            var cmd = cnn.CreateCommand();
            var init = GetInit(cmd.GetType());
            init?.Invoke(cmd);
            if (Transaction != null)
                cmd.Transaction = Transaction;
            cmd.CommandText = CommandText;
            if (CommandTimeout.HasValue)
            {
                cmd.CommandTimeout = CommandTimeout.Value;
            }
            else if (SqlMapper.Settings.CommandTimeout.HasValue)
            {
                cmd.CommandTimeout = SqlMapper.Settings.CommandTimeout.Value;
            }
            if (CommandType.HasValue)
                cmd.CommandType = CommandType.Value;
            paramReader?.Invoke(cmd, Parameters);
            return cmd;
        }

        private static SqlMapper.Link<Type, Action<IDbCommand>> commandInitCache;

        /// <summary>
        /// 获取初始化 处理委托
        /// </summary>
        /// <param name="commandType"></param>
        /// <returns></returns>
        private static Action<IDbCommand> GetInit(Type commandType)
        {
            if (commandType == null)
                return null; // GIGO
            //找缓存
            Action<IDbCommand> action;
            if (SqlMapper.Link<Type, Action<IDbCommand>>.TryGet(commandInitCache, commandType, out action))
            {
                return action;
            }

            var bindByName = GetBasicPropertySetter(commandType, "BindByName", typeof(bool));
            var initialLongFetchSize = GetBasicPropertySetter(commandType, "InitialLONGFetchSize", typeof(int));

            action = null;
            if (bindByName != null || initialLongFetchSize != null)
            {
                var method = new DynamicMethod(commandType.Name + "_init", null, new Type[] { typeof(IDbCommand) });
                var il = method.GetILGenerator();

                if (bindByName != null)
                {
                    // .BindByName = true
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, commandType);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.EmitCall(OpCodes.Callvirt, bindByName, null);
                }
                if (initialLongFetchSize != null)
                {
                    // .InitialLONGFetchSize = -1
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Castclass, commandType);
                    il.Emit(OpCodes.Ldc_I4_M1);
                    il.EmitCall(OpCodes.Callvirt, initialLongFetchSize, null);
                }
                il.Emit(OpCodes.Ret);
                action = (Action<IDbCommand>)method.CreateDelegate(typeof(Action<IDbCommand>));
            }
            // cache it
            SqlMapper.Link<Type, Action<IDbCommand>>.TryAdd(ref commandInitCache, commandType, ref action);
            return action;
        }

        private static MethodInfo GetBasicPropertySetter(Type declaringType, string name, Type expectedType)
        {
            var prop = declaringType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == expectedType && prop.GetIndexParameters().Length == 0)
            {
                return prop.GetSetMethod();
            }
            return null;
        }
    }

}
