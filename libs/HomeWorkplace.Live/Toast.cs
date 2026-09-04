namespace HomeWorkplace.Live;

public enum ToastKind { Info, Warning, Error }

public sealed record Toast(Guid Id, string Text, ToastKind Kind, DateTimeOffset At);
