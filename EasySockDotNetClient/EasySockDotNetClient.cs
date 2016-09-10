using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace EasySockDotNetClient
{
    public class Socket
    {
        private System.Net.Sockets.Socket socket;
        private Dictionary<string, Action<JObject>> events;
        private string host;
        private int port;

        public Socket(System.Net.Sockets.Socket socket, string host, int port)
        {
            events = new Dictionary<string, Action<JObject>>();
            this.socket = socket;
            this.host = host;
            this.port = port;
        }
        public void On(string evt, Action<JObject> cb)
        {
            events[evt] = cb;
        }
        public void Emit(string evt, JObject data)
        {
            Console.WriteLine("emit {0}: {1}", evt, data.ToString());
            var pkt = new JObject();
            pkt["evt"] = evt;
            pkt["data"] = data;
            socket.Send(Encoding.UTF8.GetBytes(pkt.ToString()));
        }
        public void Call(string evt, JObject data)
        {
            if (events.ContainsKey(evt))
                events[evt].Invoke(data);
        }
        public void Connect()
        {
            var connArgs = new System.Net.Sockets.SocketAsyncEventArgs();
            Action<Object, System.Net.Sockets.SocketAsyncEventArgs> connCompleted =
                (object _, System.Net.Sockets.SocketAsyncEventArgs __) =>
            {
                if (connArgs.SocketError != System.Net.Sockets.SocketError.Success)
                {
                    Console.WriteLine("Error Connecting");
                    throw new Exception(connArgs.SocketError.ToString());
                }
                byte[] buffer = new byte[4096*4096];
                StringBuilder sb = new StringBuilder("");
                var nested = 0;

                Action<Object, System.Net.Sockets.SocketAsyncEventArgs> action = null;
                Action<Object, System.Net.Sockets.SocketAsyncEventArgs> rcvCompleted =
                    (object ___, System.Net.Sockets.SocketAsyncEventArgs args) =>
                    {
                        if (args.BytesTransferred <= 0) return;
                        if (args.SocketError != System.Net.Sockets.SocketError.Success)
                        {
                            Console.WriteLine("Error Receiving");
                            throw new Exception(args.SocketError.ToString());
                        }
                        string content = Encoding.UTF8.GetString(buffer, 0, args.BytesTransferred);
                        foreach (char c in content)
                        {
                            sb.Append(c);
                            if (c == '{') nested++;
                            if (c == '}') nested--;
                            if (nested < 0)
                            {
                                Console.WriteLine("negative nesting!");
                                throw new Exception("negative nesting");
                            }
                            if (nested == 0)
                            {
                                string str = sb.ToString();
                                sb.Clear();
                                // Console.WriteLine("unwrapped nesting!");
                                // Console.WriteLine(str);
                                var obj = JObject.Parse(str);
                                Call(obj["evt"].ToString(), obj["data"] as JObject);
                            }
                        }
                        args.SetBuffer(buffer, 0, buffer.Length);
                        if (!socket.ReceiveAsync(args))
                            action.Invoke(null, args);
                    };
                action = rcvCompleted;
                var rcvArgs = new System.Net.Sockets.SocketAsyncEventArgs();
                rcvArgs.SetBuffer(buffer, 0, buffer.Length);
                rcvArgs.Completed += new EventHandler<System.Net.Sockets.SocketAsyncEventArgs>(
                    rcvCompleted
                    );
                if (!socket.ReceiveAsync(rcvArgs))
                    rcvCompleted(null, rcvArgs);
                Call("connect", null);
            };
            connArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(host), port);
            connArgs.Completed += new EventHandler<System.Net.Sockets.SocketAsyncEventArgs>(
                connCompleted
            );
            if (!socket.ConnectAsync(connArgs))
                connCompleted(null, connArgs);
        }
    }
    public static class Client
    {
        public static Socket Create(string host, int port)
        {
            var rawSocket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
            rawSocket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.KeepAlive, true);
            rawSocket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.NoDelay, true);
            return new Socket(rawSocket, host, port);
        }
    }
}
