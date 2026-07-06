using System;

namespace NetYamlForge.Services.AI.SlotFilling;

public class ScenarioResolver
{
    public string? DetectScenarioFromMessage(string message, string intent)
    {
        // 意図からシナリオをマッピング
        return intent switch
        {
            "test_drive_booking" => "test_drive",
            "price_inquiry" or "estimate_request" => "estimate",
            "service_booking" or "service_inquiry" or "maintenance" => "appointment_service",
            "trade_inquiry" => "trade_in",
            "vehicle_inquiry" => "vehicle_inquiry",
            _ => null
        };
    }
}
