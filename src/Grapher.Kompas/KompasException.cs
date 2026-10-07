using System;

namespace Grapher.Kompas;

public enum KompasErrorKind
{
    /// <summary>КОМПАС-3D не установлен (COM-класс не зарегистрирован).</summary>
    NotInstalled,
    /// <summary>КОМПАС-3D не запущен.</summary>
    NotRunning,
    /// <summary>Не удалось запустить КОМПАС-3D.</summary>
    StartFailed,
    /// <summary>Связь с КОМПАС потеряна (программу закрыли).</summary>
    ConnectionLost,
    /// <summary>Нет активного документа.</summary>
    NoActiveDocument,
    /// <summary>Активный документ — не чертёж и не фрагмент.</summary>
    Not2DDocument,
    /// <summary>КОМПАС занят: открыт диалог или выполняется команда.</summary>
    Busy,
    /// <summary>Вызов API завершился неудачей.</summary>
    ApiError
}

/// <summary>Ошибка работы с КОМПАС с сообщением, которое можно показать пользователю.</summary>
public sealed class KompasException : Exception
{
    public KompasException(KompasErrorKind kind, string message, Exception inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public KompasErrorKind Kind { get; }
}
