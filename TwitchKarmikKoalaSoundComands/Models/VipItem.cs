using System;
using System.Text.Json.Serialization;

public class VipItem {
    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("grantDate")]
    public DateTime GrantDate { get; set; }

    [JsonPropertyName("expiryDate")]
    public DateTime ExpiryDate { get; set; }

    public VipItem() { }

    public VipItem(string username, DateTime grantDate, int durationDays) {
        Username = username;
        GrantDate = grantDate;
        ExpiryDate = grantDate.AddDays(durationDays);
    }

    public bool IsExpired => DateTime.Now > ExpiryDate;
}