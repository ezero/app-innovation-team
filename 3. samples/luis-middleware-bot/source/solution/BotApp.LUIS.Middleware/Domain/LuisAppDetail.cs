﻿namespace BotApp.LUIS.Middleware.Domain
{
    public class LuisAppDetail
    {
        public string Name { get; set; }
        public string Intent { get; set; }
        public double Score { get; set; }
    }
}