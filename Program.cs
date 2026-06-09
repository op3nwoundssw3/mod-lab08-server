using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Lab08;

public sealed record Request(int ClientId, double Time);

public sealed class Client
{
    private readonly int _id;
    private readonly Random _random;

    public event EventHandler<Request>? RequestGenerated;

    public Client(int id, Server server, Random random)
    {
        _id = id;
        _random = random;
        RequestGenerated += server.ProcessRequest;
    }

    public void GenerateRequests(double requestIntensity, double simulationTime)
    {
        double currentTime = 0.0;
        int requestNumber = 0;

        while (currentTime < simulationTime)
        {
            currentTime += RandomHelper.Exponential(_random, requestIntensity);

            if (currentTime > simulationTime)
            {
                break;
            }

            RequestGenerated?.Invoke(
                this,
                new Request(_id * 1_000_000 + requestNumber, currentTime)
            );

            requestNumber++;
        }
    }
}

public sealed class Server
{
    private readonly int _channelCount;
    private readonly double _serviceIntensity;
    private readonly Random _random;

    private readonly List<double> _busyUntil = new();
    private readonly List<(double Start, double End)> _serviceIntervals = new();

    public int TotalRequests { get; private set; }
    public int ServedRequests { get; private set; }
    public int RejectedRequests { get; private set; }

    public double IdleTime { get; private set; }
    public double BusyChannelsArea { get; private set; }

    public Server(int channelCount, double serviceIntensity, Random random)
    {
        if (channelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelCount),
                "Количество каналов должно быть положительным."
            );
        }

        if (serviceIntensity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(serviceIntensity),
                "Интенсивность обслуживания должна быть положительной."
            );
        }

        _channelCount = channelCount;
        _serviceIntensity = serviceIntensity;
        _random = random;
    }

    public void ProcessRequest(object? sender, Request request)
    {
        RemoveCompletedRequests(request.Time);
        TotalRequests++;

        if (_busyUntil.Count < _channelCount)
        {
            double serviceTime = RandomHelper.Exponential(_random, _serviceIntensity);
            double finishTime = request.Time + serviceTime;

            _busyUntil.Add(finishTime);
            _serviceIntervals.Add((request.Time, finishTime));

            ServedRequests++;
        }
        else
        {
            RejectedRequests++;
        }
    }

    public void FinishSimulation(double simulationTime)
    {
        CalculateTimeStatistics(simulationTime);
        RemoveCompletedRequests(simulationTime);
    }

    private void RemoveCompletedRequests(double currentTime)
    {
        _busyUntil.RemoveAll(finishTime => finishTime <= currentTime);
    }

    private void CalculateTimeStatistics(double simulationTime)
    {
        var events = new List<(double Time, int Delta)>();

        foreach ((double start, double end) in _serviceIntervals)
        {
            if (start > simulationTime)
            {
                continue;
            }

            events.Add((start, +1));
            events.Add((Math.Min(end, simulationTime), -1));
        }

        events.Sort((left, right) => left.Time.CompareTo(right.Time));

        double lastTime = 0.0;
        int busyChannels = 0;

        IdleTime = 0.0;
        BusyChannelsArea = 0.0;

        int i = 0;

        while (i < events.Count)
        {
            double currentTime = events[i].Time;
            double interval = currentTime - lastTime;

            if (busyChannels == 0)
            {
                IdleTime += interval;
            }

            BusyChannelsArea += busyChannels * interval;

            while (i < events.Count && Math.Abs(events[i].Time - currentTime) < 1e-12)
            {
                busyChannels += events[i].Delta;
                i++;
            }

            lastTime = currentTime;
        }

        if (lastTime < simulationTime)
        {
            double interval = simulationTime - lastTime;

            if (busyChannels == 0)
            {
                IdleTime += interval;
            }

            BusyChannelsArea += busyChannels * interval;
        }
    }
}

public static class RandomHelper
{
    public static double Exponential(Random random, double intensity)
    {
        double value = 1.0 - random.NextDouble();
        return -Math.Log(value) / intensity;
    }
}

public sealed record TheoreticalResult(
    double IdleProbability,
    double RejectProbability,
    double RelativeThroughput,
    double AbsoluteThroughput,
    double AverageBusyChannels
);

public sealed record ExperimentalResult(
    double IdleProbability,
    double RejectProbability,
    double RelativeThroughput,
    double AbsoluteThroughput,
    double AverageBusyChannels,
    int TotalRequests,
    int ServedRequests,
    int RejectedRequests
);

public sealed record ResearchResult(
    double Lambda,
    TheoreticalResult Theory,
    ExperimentalResult Experiment
);

public static class Program
{
    private const int ChannelCount = 4;
    private const double ServiceIntensity = 1.0;
    private const double SimulationTime = 200_000.0;

    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        double[] lambdas =
        {
            0.5, 0.8, 1.1, 1.4, 1.7, 2.0,
            2.5, 3.0, 3.5, 4.0, 5.0, 6.0
        };

        var results = new List<ResearchResult>();
        var random = new Random(2026);

        foreach (double lambda in lambdas)
        {
            TheoreticalResult theory = CalculateTheory(
                ChannelCount,
                lambda,
                ServiceIntensity
            );

            ExperimentalResult experiment = RunExperiment(
                ChannelCount,
                lambda,
                ServiceIntensity,
                SimulationTime,
                random
            );

            results.Add(new ResearchResult(lambda, theory, experiment));
        }

        Directory.CreateDirectory("result");

        WriteCsv(results, Path.Combine("result", "results.csv"));
        WriteReport(results, "results.txt");

        PrintReportToConsole(results);

        Console.WriteLine();
        Console.WriteLine("Моделирование завершено.");
        Console.WriteLine("Созданы файлы:");
        Console.WriteLine("results.txt");
        Console.WriteLine("result/results.csv");
    }

    private static ExperimentalResult RunExperiment(
        int channelCount,
        double requestIntensity,
        double serviceIntensity,
        double simulationTime,
        Random random
    )
    {
        var server = new Server(channelCount, serviceIntensity, random);
        var client = new Client(1, server, random);

        client.GenerateRequests(requestIntensity, simulationTime);
        server.FinishSimulation(simulationTime);

        return new ExperimentalResult(
            IdleProbability: server.IdleTime / simulationTime,
            RejectProbability: server.TotalRequests == 0
                ? 0.0
                : (double)server.RejectedRequests / server.TotalRequests,
            RelativeThroughput: server.TotalRequests == 0
                ? 0.0
                : (double)server.ServedRequests / server.TotalRequests,
            AbsoluteThroughput: server.ServedRequests / simulationTime,
            AverageBusyChannels: server.BusyChannelsArea / simulationTime,
            TotalRequests: server.TotalRequests,
            ServedRequests: server.ServedRequests,
            RejectedRequests: server.RejectedRequests
        );
    }

    private static TheoreticalResult CalculateTheory(
        int channelCount,
        double requestIntensity,
        double serviceIntensity
    )
    {
        double traffic = requestIntensity / serviceIntensity;
        double sum = 0.0;

        for (int k = 0; k <= channelCount; k++)
        {
            sum += Math.Pow(traffic, k) / Factorial(k);
        }

        double p0 = 1.0 / sum;
        double pReject = Math.Pow(traffic, channelCount) / Factorial(channelCount) * p0;
        double relativeThroughput = 1.0 - pReject;
        double absoluteThroughput = requestIntensity * relativeThroughput;
        double averageBusyChannels = absoluteThroughput / serviceIntensity;

        return new TheoreticalResult(
            p0,
            pReject,
            relativeThroughput,
            absoluteThroughput,
            averageBusyChannels
        );
    }

    private static double Factorial(int n)
    {
        double result = 1.0;

        for (int i = 2; i <= n; i++)
        {
            result *= i;
        }

        return result;
    }

    private static void WriteCsv(IEnumerable<ResearchResult> results, string path)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "lambda;" +
            "theory_p0;" +
            "experiment_p0;" +
            "theory_p_reject;" +
            "experiment_p_reject;" +
            "theory_q;" +
            "experiment_q;" +
            "theory_a;" +
            "experiment_a;" +
            "theory_busy;" +
            "experiment_busy;" +
            "total;" +
            "served;" +
            "rejected"
        );

        foreach (ResearchResult r in results)
        {
            builder.AppendLine(string.Join(
                ';',
                F(r.Lambda),
                F(r.Theory.IdleProbability),
                F(r.Experiment.IdleProbability),
                F(r.Theory.RejectProbability),
                F(r.Experiment.RejectProbability),
                F(r.Theory.RelativeThroughput),
                F(r.Experiment.RelativeThroughput),
                F(r.Theory.AbsoluteThroughput),
                F(r.Experiment.AbsoluteThroughput),
                F(r.Theory.AverageBusyChannels),
                F(r.Experiment.AverageBusyChannels),
                r.Experiment.TotalRequests,
                r.Experiment.ServedRequests,
                r.Experiment.RejectedRequests
            ));
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static void WriteReport(IEnumerable<ResearchResult> results, string path)
    {
        var builder = BuildReport(results);
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static void PrintReportToConsole(IEnumerable<ResearchResult> results)
    {
        Console.WriteLine(BuildReport(results).ToString());
    }

    private static StringBuilder BuildReport(IEnumerable<ResearchResult> results)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Lab08. Моделирование СМО: клиент-сервер");
        builder.AppendLine();

        builder.AppendLine($"Количество каналов n = {ChannelCount}");
        builder.AppendLine($"Интенсивность обслуживания μ = {F(ServiceIntensity)}");
        builder.AppendLine($"Время моделирования T = {F(SimulationTime)}");
        builder.AppendLine();

        builder.AppendLine("Использованные теоретические формулы для M/M/n/n:");
        builder.AppendLine("a = λ / μ");
        builder.AppendLine("P0 = 1 / Σ(a^k / k!), k = 0..n");
        builder.AppendLine("Pотк = (a^n / n!) * P0");
        builder.AppendLine("Q = 1 - Pотк");
        builder.AppendLine("A = λ * Q");
        builder.AppendLine("Nзан = A / μ");
        builder.AppendLine();

        builder.AppendLine(
            "λ      P0 теор.  P0 эксп.   Pотк теор. Pотк эксп. " +
            "Q теор.  Q эксп.   A теор.  A эксп.   Nзан теор. Nзан эксп."
        );

        foreach (ResearchResult r in results)
        {
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,4:F1}   {1,8:F4}  {2,8:F4}  {3,9:F4}  {4,9:F4}  {5,7:F4}  {6,7:F4}  {7,8:F4}  {8,8:F4}  {9,10:F4}  {10,10:F4}",
                r.Lambda,
                r.Theory.IdleProbability,
                r.Experiment.IdleProbability,
                r.Theory.RejectProbability,
                r.Experiment.RejectProbability,
                r.Theory.RelativeThroughput,
                r.Experiment.RelativeThroughput,
                r.Theory.AbsoluteThroughput,
                r.Experiment.AbsoluteThroughput,
                r.Theory.AverageBusyChannels,
                r.Experiment.AverageBusyChannels
            ));
        }

        builder.AppendLine();
        builder.AppendLine("Вывод:");
        builder.AppendLine(
            "Экспериментальные значения близки к теоретическим. " +
            "При увеличении интенсивности входного потока λ вероятность простоя уменьшается, " +
            "вероятность отказа растет, относительная пропускная способность падает. " +
            "Абсолютная пропускная способность и среднее число занятых каналов растут до насыщения системы."
        );

        return builder;
    }

    private static string F(double value)
    {
        return value.ToString("F6", CultureInfo.InvariantCulture);
    }
}
