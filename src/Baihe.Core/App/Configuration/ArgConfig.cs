using System;
using Baihe.Core.Utils;

namespace Baihe.Core.App.Configuration;

public class ArgConfig<TValue> : ParameterizedProperty<object, TValue>
{
    public ArgConfig(Func<object?, TValue> getter, Action<object?, TValue> setter)
    {
        GetValue = getter;
        SetValue = setter;
    }
}
