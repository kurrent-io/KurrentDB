using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Collections.Generic;

namespace KurrentDB.Surge.Testing.Containers;

/// <summary>
/// Used to provide a network port for testing.
/// Can be called by multiple tests concurrently.
/// </summary>
public static class NetworkPortProvider {
    public const int DefaultEsdbPort = 2113;

    const int DefaultDelayMs = 100;
    const int PortLimit      = 65535;

    static SortedSet<int> ProvidedPorts { get; } = [];

    static readonly SemaphoreSlim Semaphore = new(1, 1);

    public static async Task<int> GetNextAvailablePort(int delay = DefaultDelayMs, int skip = 0) {
        await Semaphore.WaitAsync();

        if (skip > 0)
            Enumerable.Range(DefaultEsdbPort, skip)
                .Where(port => !ProvidedPorts.Contains(port))
                .ForEach(port => ProvidedPorts.Add(port));

        try {
            for (var nextPort = DefaultEsdbPort + skip; nextPort < PortLimit; nextPort++) {
                if (ProvidedPorts.Contains(nextPort))
                    continue;

                if (IsPortAvailable(nextPort)) {
                    ProvidedPorts.Add(nextPort);
                    return nextPort;
                }

                await Task.Delay(delay);
            }

            throw new InvalidOperationException("Failed to acquire a network port.");
        }
        finally {
            Semaphore.Release();
        }
    }

    public static async Task<int[]> GetNumberOfPorts(int numberOfPorts) {
        var ports = new int[numberOfPorts];
        for (var i = 0; i < numberOfPorts; i++)
            ports[i] = await GetNextAvailablePort();

        return ports;
    }

    public static async Task ReleasePorts(params int[] ports) {
        await Semaphore.WaitAsync();

        try {
            foreach (var port in ports)
                ProvidedPorts.Remove(port);
        }
        finally {
            Semaphore.Release();
        }
    }

    public static Task ReleasePort(int port) => ReleasePorts(port);

    static bool IsPortAvailable(int port) {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try {
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException) {
            // ignored
        }

        return false;
    }
}
