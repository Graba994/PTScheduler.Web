namespace PTScheduler.Web.Services;

public enum ToastLevel { Success, Error, Warning, Info }

public sealed class ToastService
{
    public event Action<ToastItem>? OnShow;

    public void Success(string message) => Show(ToastLevel.Success, message);
    public void Error(string message) => Show(ToastLevel.Error, message);
    public void Warning(string message) => Show(ToastLevel.Warning, message);
    public void Info(string message) => Show(ToastLevel.Info, message);

    private void Show(ToastLevel level, string message)
        => OnShow?.Invoke(new ToastItem(level, message));
}

public sealed record ToastItem(ToastLevel Level, string Message);
