namespace Shammy.Notifications;

/// <summary>Activity payload for a notification on display; <see cref="More"/> counts others that arrived meanwhile.</summary>
internal sealed record NotificationPayload(ShammyNotification Notification, int More);
