using System.Runtime.CompilerServices;

// 禁用运行时封送，提高性能
[assembly: DisableRuntimeMarshalling]

// 允许测试项目访问内部成员
[assembly: InternalsVisibleTo("Baihe.Core.Test")]
