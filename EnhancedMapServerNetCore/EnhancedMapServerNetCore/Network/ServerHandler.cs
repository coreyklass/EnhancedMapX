﻿using EnhancedMapServerNetCore.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace EnhancedMapServerNetCore.Network
{
    public class ServerHandler
    {
        private readonly ReusableArray<byte> _bufferPool = new ReusableArray<byte>(4, 4096);
        private Queue<Session> _workingQueue, _queue;
        private Server _server;

        const int MAX = 4096;

        public ServerHandler(Server server)
        {
            _server = server;
            _workingQueue = new Queue<Session>();
            _queue = new Queue<Session>();
        }

        public void Enqueue(Session session)
        {
            lock (this)
                _queue.Enqueue(session);
            Core.Set();
        }

        public void Slice()
        {
            CheckIncomingConnections();

            lock (this)
            {
                Queue<Session> t = _workingQueue;
                _workingQueue = _queue;
                _queue = t;
            }

            while (_workingQueue.Count > 0)
            {
                Session s = _workingQueue.Dequeue();
                if (s != null && s.IsRunning)
                    HandlePackets(s);
            }
        }


        private void HandlePackets(Session s)
        {
            if (s == null)
                return;


            CircularBuffer buffer = s.Buffer;

            if (buffer == null || buffer.Length <= 0)
                return;

            lock (buffer)
            {
                int length = buffer.Length;

                while (length > 0 && s.IsRunning)
                {
                    byte messageID = buffer.GetPacketID();
                    int messageLength = buffer.GetPacketLength();

                    if (messageLength < 3)
                    {
                        s.Dispose();
                        break;
                    }

                    if (length < messageLength)
                        break;

                    byte[] data = MAX >= messageLength ? _bufferPool.GetSegment() : new byte[messageLength];

                    messageLength = buffer.Dequeue(data, 0, messageLength);

                    Packet packet = new Packet(data, messageID, messageLength);
                    PacketHandlers.Handlers.OnPacket(s, packet);

                    length = buffer.Length;

                    if (MAX >= messageLength)
                        _bufferPool.Free(data);
                }
            }
        }

        private void CheckIncomingConnections()
        {
            Socket[] accepted = _server.GetAwaitingSockets();

            for (int i = 0; i < accepted.Length; i++)
            {
                var args = _server.GetAvailableArgs();
                if (args.Length == 0)
                {
                    Log.Message(LogTypes.Warning, "Reached max connections number!");
                    
                    try
                    {
                        accepted[i].Shutdown(SocketShutdown.Both);
                    }
                    catch { }

                    try
                    {
                        accepted[i].Close();
                    }
                    catch { }
                }
                else
                {

                    Session session = new Session(accepted[i], this)
                    {
                        Args = args,
                        Disconnect = _server.Disconnect
                    };

                    session.Accept();

                    _server.Increase(session);

                    Log.Message(LogTypes.Info, "New session connected.");
                }

            }
        }
    }
}