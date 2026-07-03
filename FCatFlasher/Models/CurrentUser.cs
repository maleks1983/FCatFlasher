namespace FCatFlasher.Models
{
/// <summary>
/// Поточний оператор застосунку та ознака офлайн-режиму.
/// </summary>
    internal class CurrentUser
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsOffline { get; set; }
    }
}
