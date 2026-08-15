using BidEuchre.Protocol;
using BidEuchre.SampleBot;

var host = new EngineHost(new SimpleBot());
await host.RunAsync(Console.In, Console.Out);
