using System;
using NodaTime;

var lower = new LocalTime(21, 30);  // 21:30
var upper = new LocalTime(3, 0);    // 03:00
var time = new LocalTime(22, 45);   // 22:45

var timeMinutes = time.TickOfDay / NodaConstants.TicksPerMinute;
var lowerMinutes = lower.TickOfDay / NodaConstants.TicksPerMinute;
var upperMinutes = upper.TickOfDay / NodaConstants.TicksPerMinute;

Console.WriteLine($"Time: {time} = {timeMinutes} minutes");
Console.WriteLine($"Lower: {lower} = {lowerMinutes} minutes");
Console.WriteLine($"Upper: {upper} = {upperMinutes} minutes");
Console.WriteLine($"Lower > Upper: {lowerMinutes > upperMinutes}");
Console.WriteLine($"Time >= Lower: {timeMinutes >= lowerMinutes}");
Console.WriteLine($"Time <= Upper: {timeMinutes <= upperMinutes}");

var distToLower = timeMinutes - lowerMinutes;
var distToUpper = (24 * 60 - timeMinutes) + upperMinutes;
Console.WriteLine($"Distance to lower: {distToLower}");
Console.WriteLine($"Distance to upper: {distToUpper}");
Console.WriteLine($"Should clamp to: {(distToLower < distToUpper ? "lower" : "upper")}");
