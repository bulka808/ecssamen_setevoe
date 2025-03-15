using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class VotingServer
{
    private static Dictionary<string, int> votes = new Dictionary<string, int>();
    private static List<TcpClient> clients = new List<TcpClient>();
    private static Dictionary<string, string> users = new Dictionary<string, string>(); // username -> vote
    private static object lockObject = new object();
    private static bool isRunning = true;
    private static int votingTime = 150; // Время голосования в секундах
    private static HashSet<string> adminUsers = new HashSet<string> { "admin" }; // Список администраторов

    static void Main(string[] args)
    {
        Console.WriteLine("Starting Voting Server...");
        TcpListener listener = new TcpListener(IPAddress.Any, 5000);
        listener.Start();

        
        Thread timerThread = new Thread(() =>
        {
            Thread.Sleep(votingTime * 1000);
            lock (lockObject)
            {
                Console.WriteLine("Voting time is over!");
                BroadcastResults(true);
                isRunning = false;
            }
        });
        timerThread.Start();

        while (isRunning)
        {
            if (!listener.Pending())
            {
                Thread.Sleep(100);
                continue;
            }

            TcpClient client = listener.AcceptTcpClient();
            clients.Add(client);
            Thread clientThread = new Thread(() => HandleClient(client));
            clientThread.Start();
        }

        listener.Stop();
        Console.WriteLine("Server stopped.");
    }

    private static void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];
        string username = null;

        try
        {
            
            SendData(stream, "Enter your username:");
            username = ReceiveData(stream);
            if (string.IsNullOrEmpty(username) || users.ContainsKey(username))
            {
                SendData(stream, "Username is invalid or already taken. Disconnecting...");
                client.Close();
                return;
            }

            users[username] = null;
            SendData(stream, "Welcome, " + username + "!");


            SendOptions(stream);

            while (isRunning)
            {
                string message = ReceiveData(stream);
                if (message == "/exit")
                {
                    break;
                }
                else if (message.StartsWith("/vote"))
                {
                    string option = message.Split(' ')[1];
                    if (votes.ContainsKey(option))
                    {
                        lock (lockObject)
                        {
                            if (users[username] != null)
                            {
                                votes[users[username]]--;
                            }
                            votes[option]++;
                            users[username] = option;
                        }
                        SendData(stream, "Your vote has been recorded.");
                        BroadcastResults(false);
                    }
                    else
                    {
                        SendData(stream, "Invalid option.");
                    }
                }
                else if (message == "/results")
                {
                    SendResults(stream);
                }
                else if (message.StartsWith("/add") && adminUsers.Contains(username))
                {
                    string option = message.Split(' ')[1];
                    AddOption(option);
                    SendData(stream, $"Option '{option}' added successfully.");
                    BroadcastOptions();
                }
                else if (message.StartsWith("/remove") && adminUsers.Contains(username))
                {
                    string option = message.Split(' ')[1];
                    RemoveOption(option);
                    SendData(stream, $"Option '{option}' removed successfully.");
                    BroadcastOptions();
                }
                else
                {
                    SendData(stream, "Unknown command.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client error: {ex.Message}");
        }
        finally
        {
            lock (lockObject)
            {
                clients.Remove(client);
                users.Remove(username);
            }
            client.Close();
        }
    }

    private static void AddOption(string option)
    {
        lock (lockObject)
        {
            if (!votes.ContainsKey(option))
            {
                votes[option] = 0;
            }
        }
    }

    private static void RemoveOption(string option)
    {
        lock (lockObject)
        {
            if (votes.ContainsKey(option))
            {
                votes.Remove(option);
            }
        }
    }

    private static void BroadcastOptions()
    {
        StringBuilder options = new StringBuilder("Updated options:\n");
        foreach (var option in votes.Keys)
        {
            options.AppendLine(option);
        }

        string message = options.ToString();
        lock (lockObject)
        {
            foreach (var client in clients)
            {
                try
                {
                    SendData(client.GetStream(), message);
                }
                catch{ }
            }
        }
    }

    private static void SendOptions(NetworkStream stream)
    {
        StringBuilder options = new StringBuilder("Available options:\n");
        foreach (var option in votes.Keys)
        {
            options.AppendLine(option);
        }
        SendData(stream, options.ToString());
    }

    private static void SendResults(NetworkStream stream)
    {
        StringBuilder results = new StringBuilder("Current results:\n");
        foreach (var pair in votes)
        {
            results.AppendLine($"{pair.Key}: {pair.Value} votes");
        }
        SendData(stream, results.ToString());
    }

    private static void BroadcastResults(bool isFinal)
    {
        StringBuilder results = new StringBuilder(isFinal ? "Final results:\n" : "Updated results:\n");
        foreach (var pair in votes)
        {
            results.AppendLine($"{pair.Key}: {pair.Value} votes");
        }

        string message = results.ToString();
        lock (lockObject)
        {
            foreach (var client in clients)
            {
                try
                {
                    SendData(client.GetStream(), message);
                }
                catch{ }
            }
        }
    }

    private static void SendData(NetworkStream stream, string message)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        stream.Write(data, 0, data.Length);
    }

    private static string ReceiveData(NetworkStream stream)
    {
        byte[] buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
    }
}
