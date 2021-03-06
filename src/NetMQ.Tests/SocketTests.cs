﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

// ReSharper disable ExceptionNotDocumented

namespace NetMQ.Tests
{
    [TestFixture]
    public class SocketTests
    {
        [Test, ExpectedException(typeof(AgainException)), Obsolete]
        public void CheckReceiveAgainException()
        {
            using (var context = NetMQContext.Create())
            using (var router = context.CreateRouterSocket())
            {
                router.BindRandomPort("tcp://127.0.0.1");

                router.Receive(SendReceiveOptions.DontWait);
            }
        }

        [Test, ExpectedException(typeof(AgainException))]
        public void CheckSendAgainException()
        {
            using (var context = NetMQContext.Create())
            using (var router = context.CreateRouterSocket())
            using (var dealer = context.CreateDealerSocket())
            {
                var port = router.BindRandomPort("tcp://127.0.0.1");
                router.Options.Linger = TimeSpan.Zero;

                dealer.Options.SendHighWatermark = 1;
                dealer.Options.Linger = TimeSpan.Zero;
                dealer.Connect("tcp://127.0.0.1:" + port);
                
#pragma warning disable 618
                dealer.Send("1", dontWait: true, sendMore: false);
                dealer.Send("2", dontWait: true, sendMore: false);
#pragma warning restore 618
            }
        }

        [Test]
        public void CheckSendTryAgain()
        {
            using (var context = NetMQContext.Create())
            using (var router = context.CreateRouterSocket())
            using (var dealer = context.CreateDealerSocket())
            {
                var port = router.BindRandomPort("tcp://127.0.0.1");
                router.Options.Linger = TimeSpan.Zero;

                dealer.Options.SendHighWatermark = 1;
                dealer.Options.Linger = TimeSpan.Zero;
                dealer.Connect("tcp://127.0.0.1:" + port);

                Thread.Sleep(100);

                Assert.IsTrue(dealer.TrySendFrame("1"));                                
                Assert.IsFalse(dealer.TrySendFrame("2"));                                
            }
        }

        [Test]
        public void LargeMessage()
        {
            using (var context = NetMQContext.Create())
            using (var pub = context.CreatePublisherSocket())
            using (var sub = context.CreateSubscriberSocket())
            {
                var port = pub.BindRandomPort("tcp://127.0.0.1");
                sub.Connect("tcp://127.0.0.1:" + port);
                sub.Subscribe("");

                Thread.Sleep(100);

                var msg = new byte[300];

                pub.SendFrame(msg);

                byte[] msg2 = sub.ReceiveFrameBytes();

                Assert.AreEqual(300, msg2.Length);
            }
        }

        [Test]
        public void ReceiveMessageWithTimeout()
        {
            using (var context = NetMQContext.Create())
            {
                var pubSync = new AutoResetEvent(false);
                var payload = new byte[300];
                const int waitTime = 500;

                var t1 = new Task(() =>
                {
                    using (var pubSocket = context.CreatePublisherSocket())
                    {
                        pubSocket.Bind("tcp://127.0.0.1:12345");
                        pubSync.WaitOne();
                        Thread.Sleep(waitTime);
                        pubSocket.SendFrame(payload);
                        pubSync.WaitOne();
                    }
                }, TaskCreationOptions.LongRunning);

                var t2 = new Task(() =>
                {
                    using (var subSocket = context.CreateSubscriberSocket())
                    {
                        subSocket.Connect("tcp://127.0.0.1:12345");
                        subSocket.Subscribe("");
                        Thread.Sleep(100);
                        pubSync.Set();

                        NetMQMessage msg = null;
                        Assert.IsFalse(subSocket.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref msg));

                        Assert.IsTrue(subSocket.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(waitTime), ref msg));
                        Assert.NotNull(msg);
                        Assert.AreEqual(1, msg.FrameCount);
                        Assert.AreEqual(300, msg.First.MessageSize);
                        pubSync.Set();
                    }
                }, TaskCreationOptions.LongRunning);

                t1.Start();
                t2.Start();

                Task.WaitAll(t1, t2);
            }
        }

        [Test]
        public void LargeMessageLittleEndian()
        {
            using (var context = NetMQContext.Create())
            using (var pub = context.CreatePublisherSocket())
            using (var sub = context.CreateSubscriberSocket())
            {
                pub.Options.Endian = Endianness.Little;
                sub.Options.Endian = Endianness.Little;

                var port = pub.BindRandomPort("tcp://127.0.0.1");
                sub.Connect("tcp://127.0.0.1:" + port);

                sub.Subscribe("");

                Thread.Sleep(100);

                var msg = new byte[300];

                pub.SendFrame(msg);

                byte[] msg2 = sub.ReceiveFrameBytes();

                Assert.AreEqual(300, msg2.Length);
            }
        }

        [Test]
        public void TestKeepAlive()
        {
            // there is no way to test tcp keep alive without disconnect the cable, we just testing that is not crashing the system
            using (var context = NetMQContext.Create())
            using (var rep = context.CreateResponseSocket())
            using (var req = context.CreateRequestSocket())
            {
                rep.Options.TcpKeepalive = true;
                rep.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(5);
                rep.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(1);

                req.Options.TcpKeepalive = true;
                req.Options.TcpKeepaliveIdle = TimeSpan.FromSeconds(5);
                req.Options.TcpKeepaliveInterval = TimeSpan.FromSeconds(1);

                var port = rep.BindRandomPort("tcp://127.0.0.1");
                req.Connect("tcp://127.0.0.1:" + port);

                bool more;

                req.SendFrame("1");

                Assert.AreEqual("1", rep.ReceiveFrameString(out more));
                Assert.IsFalse(more);

                rep.SendFrame("2");

                Assert.AreEqual("2", req.ReceiveFrameString(out more));
                Assert.IsFalse(more);

                Assert.IsTrue(req.Options.TcpKeepalive);
                Assert.AreEqual(TimeSpan.FromSeconds(5), req.Options.TcpKeepaliveIdle);
                Assert.AreEqual(TimeSpan.FromSeconds(1), req.Options.TcpKeepaliveInterval);

                Assert.IsTrue(rep.Options.TcpKeepalive);
                Assert.AreEqual(TimeSpan.FromSeconds(5), rep.Options.TcpKeepaliveIdle);
                Assert.AreEqual(TimeSpan.FromSeconds(1), rep.Options.TcpKeepaliveInterval);
            }
        }

        [Test]
        public void MultipleLargeMessages()
        {
            var largeMessage = new byte[12000];

            for (int i = 0; i < 12000; i++)
            {
                largeMessage[i] = (byte)(i % 256);
            }

            using (var context = NetMQContext.Create())
            using (var pub = context.CreatePublisherSocket())
            using (var sub = context.CreateSubscriberSocket())
            {
                var port = pub.BindRandomPort("tcp://127.0.0.1");
                sub.Connect("tcp://127.0.0.1:" + port);
                sub.Subscribe("");

                Thread.Sleep(1000);

                pub.SendFrame("");
                sub.SkipFrame();

                for (int i = 0; i < 100; i++)
                {
                    pub.SendFrame(largeMessage);

                    byte[] recvMesage = sub.ReceiveFrameBytes();

                    for (int j = 0; j < 12000; j++)
                    {
                        Assert.AreEqual(largeMessage[j], recvMesage[j]);
                    }
                }
            }
        }

        [Test]
        public void LargerBufferLength()
        {
            var largerBuffer = new byte[256];
            {
                largerBuffer[124] = 0xD;
                largerBuffer[125] = 0xE;
                largerBuffer[126] = 0xE;
                largerBuffer[127] = 0xD;
            }

            using (var context = NetMQContext.Create())
            using (var pub = context.CreatePublisherSocket())
            using (var sub = context.CreateSubscriberSocket())
            {
                var port = pub.BindRandomPort("tcp://127.0.0.1");
                sub.Connect("tcp://127.0.0.1:" + port);
                sub.Subscribe("");

                Thread.Sleep(100);

                pub.SendFrame(largerBuffer, 128);

                byte[] recvMesage = sub.ReceiveFrameBytes();

                Assert.AreEqual(128, recvMesage.Length);
                Assert.AreEqual(0xD, recvMesage[124]);
                Assert.AreEqual(0xE, recvMesage[125]);
                Assert.AreEqual(0xE, recvMesage[126]);
                Assert.AreEqual(0xD, recvMesage[127]);

                Assert.AreNotEqual(largerBuffer.Length, recvMesage.Length);
            }
        }

        [Test]
        public void RawSocket()
        {
            using (var context = NetMQContext.Create())
            using (var router = context.CreateRouterSocket())
            using (var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                router.Options.RouterRawSocket = true;
                var port = router.BindRandomPort("tcp://127.0.0.1");

                clientSocket.Connect("127.0.0.1", port);
                clientSocket.NoDelay = true;

                byte[] clientMessage = Encoding.ASCII.GetBytes("HelloRaw");

                int bytesSent = clientSocket.Send(clientMessage);
                Assert.Greater(bytesSent, 0);

                byte[] id = router.ReceiveFrameBytes();
                byte[] message = router.ReceiveFrameBytes();

                router.SendMoreFrame(id).SendMoreFrame(message); // SendMore option is ignored

                var buffer = new byte[16];

                int bytesRead = clientSocket.Receive(buffer);
                Assert.Greater(bytesRead, 0);

                Assert.AreEqual(Encoding.ASCII.GetString(buffer, 0, bytesRead), "HelloRaw");
            }
        }

        [Test]
        public void BindRandom()
        {
            using (var context = NetMQContext.Create())
            using (var randomDealer = context.CreateDealerSocket())
            using (var connectingDealer = context.CreateDealerSocket())
            {
                int port = randomDealer.BindRandomPort("tcp://*");
                connectingDealer.Connect("tcp://127.0.0.1:" + port);

                randomDealer.SendFrame("test");

                Assert.AreEqual("test", connectingDealer.ReceiveFrameString());
            }
        }

        [Test]
        public void BindToLocal()
        {
            var validAliasesForLocalHost = new[] { "127.0.0.1", "localhost", Dns.GetHostName() };

            foreach (var alias in validAliasesForLocalHost)
            {
                using (var context = NetMQContext.Create())
                using (var localDealer = context.CreateDealerSocket())
                using (var connectingDealer = context.CreateDealerSocket())
                {
                    var port = localDealer.BindRandomPort("tcp://*");
                    connectingDealer.Connect(string.Format("tcp://{0}:{1}", alias, port));

                    localDealer.SendFrame("test");

                    Assert.AreEqual("test", connectingDealer.ReceiveFrameString());
                    Console.WriteLine(alias + " connected ");
                }
            }
        }

        [Test, Category("IPv6")]
        public void Ipv6ToIpv4()
        {
            using (var context = NetMQContext.Create())
            using (var localDealer = context.CreateDealerSocket())
            using (NetMQSocket connectingDealer = context.CreateDealerSocket())
            {
                localDealer.Options.IPv4Only = false;
                var port = localDealer.BindRandomPort(string.Format("tcp://*"));

                connectingDealer.Connect(string.Format("tcp://{0}:{1}", IPAddress.Loopback, port));

                connectingDealer.SendFrame("test");

                Assert.AreEqual("test", localDealer.ReceiveFrameString());
            }
        }

        [Test, Category("IPv6")]
        public void Ipv6ToIpv6()
        {
            using (var context = NetMQContext.Create())
            using (var localDealer = context.CreateDealerSocket())
            using (var connectingDealer = context.CreateDealerSocket())
            {
                localDealer.Options.IPv4Only = false;
                var port = localDealer.BindRandomPort(string.Format("tcp://*"));

                connectingDealer.Options.IPv4Only = false;
                connectingDealer.Connect(string.Format("tcp://{0}:{1}", IPAddress.IPv6Loopback, port));

                connectingDealer.SendFrame("test");

                Assert.AreEqual("test", localDealer.ReceiveFrameString());
            }
        }

        [Test]
        public void HasInTest()
        {
            using (var context = NetMQContext.Create())
            using (var server = context.CreateRouterSocket())
            using (var client = context.CreateDealerSocket())
            {
                var port = server.BindRandomPort("tcp://*");

                // no one sent a message so it should be false
                Assert.IsFalse(server.HasIn);

                client.Connect("tcp://localhost:" + port);

                // wait for the client to connect
                Thread.Sleep(100);

                // now we have one client connected but didn't send a message yet
                Assert.IsFalse(server.HasIn);

                client.SendFrame("1");

                // wait for the message to arrive
                Thread.Sleep(100);

                // the has in should indicate a message is ready
                Assert.IsTrue(server.HasIn);

                server.SkipFrame(); // identity
                string message = server.ReceiveFrameString();

                Assert.AreEqual(message, "1");

                // we read the message, it should false again
                Assert.IsFalse(server.HasIn);
            }
        }

        [Test]
        public void DisposeImmediately()
        {
            using (var context = NetMQContext.Create())
            using (var server = context.CreateDealerSocket())
            {
                server.BindRandomPort("tcp://*");
            }
        }

        [Test]
        public void HasOutTest()
        {
            using (var context = NetMQContext.Create())
            using (var server = context.CreateDealerSocket())
            {
                using (var client = context.CreateDealerSocket())
                {
                    var port = server.BindRandomPort("tcp://*");

                    // no client is connected so we don't have out
                    Assert.IsFalse(server.HasOut);

                    Assert.IsFalse(client.HasOut);

                    client.Connect("tcp://localhost:" + port);

                    Thread.Sleep(200);

                    // client is connected so server should have out now, client as well
                    Assert.IsTrue(server.HasOut);
                    Assert.IsTrue(client.HasOut);
                }

                //Thread.Sleep(2000);
                // client is disposed,server shouldn't have out now
                //Assert.IsFalse(server.HasOut);
            }
        }

        [Test, TestCase("tcp"), TestCase("inproc")]
        public void Disconnect(string protocol)
        {
            using (var context = NetMQContext.Create())
            using (var server1 = context.CreateDealerSocket())
            using (var server2 = context.CreateDealerSocket())
            using (var client = context.CreateDealerSocket())
            {
                string address2;

                if (protocol == "tcp")
                {
                    var port1 = server1.BindRandomPort("tcp://localhost");
                    var port2 = server2.BindRandomPort("tcp://localhost");

                    client.Connect("tcp://localhost:" + port1);
                    client.Connect("tcp://localhost:" + port2);

                    address2 = "tcp://localhost:" + port2;
                }
                else
                {
                    server1.Bind("inproc://localhost1");
                    server2.Bind("inproc://localhost2");

                    client.Connect("inproc://localhost1");
                    client.Connect("inproc://localhost2");

                    address2 = "inproc://localhost2";
                }

                Thread.Sleep(100);

                // we should be connected to both server
                client.SendFrame("1");
                client.SendFrame("2");

                // make sure client is connected to both servers
                server1.SkipFrame();
                server2.SkipFrame();

                // disconnect from server2, server 1 should receive all messages
                client.Disconnect(address2);
                Thread.Sleep(100);

                client.SendFrame("1");
                client.SendFrame("2");

                server1.SkipFrame();
                server1.SkipFrame();
            }
        }

        [Test, TestCase("tcp"), TestCase("inproc")]
        public void Unbind(string protocol)
        {
            using (var context = NetMQContext.Create())
            using (var server = context.CreateDealerSocket())
            {
                string address1, address2;

                // just making sure can bind on both addresses
                using (var client1 = context.CreateDealerSocket())
                using (var client2 = context.CreateDealerSocket())
                {
                    if (protocol == "tcp")
                    {
                        var port1 = server.BindRandomPort("tcp://localhost");
                        var port2 = server.BindRandomPort("tcp://localhost");

                        address1 = "tcp://localhost:" + port1;
                        address2 = "tcp://localhost:" + port2;

                        client1.Connect(address1);
                        client2.Connect(address2);
                    }
                    else
                    {
                        Debug.Assert(protocol == "inproc");

                        address1 = "inproc://localhost1";
                        address2 = "inproc://localhost2";

                        server.Bind(address1);
                        server.Bind(address2);

                        client1.Connect(address1);
                        client2.Connect(address2);
                    }

                    Thread.Sleep(100);

                    // we should be connected to both server
                    client1.SendFrame("1");
                    client2.SendFrame("2");

                    // the server receive from both
                    server.SkipFrame();
                    server.SkipFrame();
                }

                // unbind second address
                server.Unbind(address2);
                Thread.Sleep(100);

                using (var client1 = context.CreateDealerSocket())
                using (var client2 = context.CreateDealerSocket())
                {
                    client1.Options.DelayAttachOnConnect = true;
                    client1.Connect(address1);
                    
                    client2.Options.DelayAttachOnConnect = true;

                    if (protocol == "tcp")
                    {
                        client2.Connect(address2);

                        client1.SendFrame("1");
                        server.SkipFrame();

                        Assert.IsFalse(client2.TrySendFrame(TimeSpan.FromSeconds(2), "2"));                        
                    }
                    else
                    {
                        Assert.Throws<EndpointNotFoundException>(() => { client2.Connect(address2); });
                    }
                }
            }
        }

        [Test]
        public void ASubscriberSocketThatGetDisconnectedBlocksItsContextFromBeingDisposed()
        {
            // NOTE two contexts here

            using (var subContext = NetMQContext.Create())
            using (var pubContext = NetMQContext.Create())
            using (var pub = pubContext.CreatePublisherSocket())
            using (var sub = subContext.CreateSubscriberSocket())
            {
                pub.Options.Linger = TimeSpan.FromSeconds(0);                
                sub.Options.Linger = TimeSpan.FromSeconds(0);

                sub.Connect("tcp://localhost:12345");
                sub.Subscribe("");

//                Thread.Sleep(1000);

                pub.Bind("tcp://localhost:12345");

                // NOTE the test fails if you remove this sleep
                Thread.Sleep(1000);

                for (var i = 0; i < 100; i++)
                {
                    var sent = "msg-" + i;
  
                    pub.SendFrame(sent);

                    string received;
                    Assert.IsTrue(sub.TryReceiveFrameString(TimeSpan.FromMilliseconds(100), out received));
                    Assert.AreEqual(sent, received);
                }

                pub.Close();

//                Thread.Sleep(1000);
            }
        }

        [Test]
        public void BindRandomThenUnbind()
        {
            using (var context = NetMQContext.Create())
            using (var pub = context.CreatePublisherSocket())
            {
                var port = pub.BindRandomPort("tcp://localhost");

                pub.Unbind("tcp://localhost:" + port);
            }

            using (var context = NetMQContext.Create())
            using (var pub = context.CreatePublisherSocket())
            {
                var port = pub.BindRandomPort("tcp://*");

                pub.Unbind("tcp://*:" + port);
            }

            using (var context = NetMQContext.Create())
            using (var pub = context.CreatePublisherSocket())
            {
                var port1 = pub.BindRandomPort("tcp://*");
                var port2 = pub.BindRandomPort("tcp://*");
                var port3 = pub.BindRandomPort("tcp://*");

                pub.Unbind("tcp://*:" + port1);
                pub.Unbind("tcp://*:" + port2);
                pub.Unbind("tcp://*:" + port3);
            }
        }

        [Test]
        public void ReconnectOnRouterBug()
        {
            using (var context = NetMQContext.Create())
            {
                using (var dealer = context.CreateDealerSocket())
                {
                    dealer.Options.Identity = Encoding.ASCII.GetBytes("dealer");
                    dealer.Bind("tcp://localhost:6667");

                    using (var router = context.CreateRouterSocket())
                    {
                        router.Options.RouterMandatory = true;
                        router.Connect("tcp://localhost:6667");
                        Thread.Sleep(100);

                        router.SendMoreFrame("dealer").SendFrame("Hello");
                        var message = dealer.ReceiveFrameString();
                        Assert.That(message == "Hello");

                        router.Disconnect("tcp://localhost:6667");                                                
                        Thread.Sleep(1000);
                        router.Connect("tcp://localhost:6667");
                        Thread.Sleep(100);

                        router.SendMoreFrame("dealer").SendFrame("Hello");
                        message = dealer.ReceiveFrameString();
                        Assert.That(message == "Hello");
                    }
                }
            }
        }

        [Test]
        public void InprocRouterDealerTest()
        {
            // The main thread simply starts several clients and a server, and then
            // waits for the server to finish.
            List<Thread> workers = new List<Thread>();
            byte[] s_ReadyMsg = Encoding.UTF8.GetBytes("RDY");
            Queue<byte[]> s_FreeWorkers = new Queue<byte[]>();

            using (var context = NetMQContext.Create())
            {
                using (var backendsRouter = context.CreateRouterSocket())
                {
                    backendsRouter.Options.Identity = Guid.NewGuid().ToByteArray();
                    backendsRouter.Bind("inproc://backend");

                    backendsRouter.ReceiveReady += (o, e)=>
                    {
                        // Handle worker activity on backend
                        while (e.Socket.HasIn)
                        {
                            var msg = e.Socket.ReceiveMultipartMessage();
                            var idRouter = msg.Pop();
                            // forget the empty frame
                            if (msg.First.IsEmpty)
                                msg.Pop();

                            var id = msg.Pop();
                            if (msg.First.IsEmpty)
                                msg.Pop();

                            if (msg.FrameCount == 1)
                            {
                                // worker send RDY message queue his Identity to the free workers queue
                                if (s_ReadyMsg[0] ==msg[0].Buffer[0] &&
                                    s_ReadyMsg[1] ==msg[0].Buffer[1] &&
                                    s_ReadyMsg[2] ==msg[0].Buffer[2])
                                {
                                    lock (s_FreeWorkers)
                                    {
                                        s_FreeWorkers.Enqueue(id.Buffer);
                                    }
                                }
                            }
                        }
                    };

                    Poller poller = new Poller();
                    poller.AddSocket(backendsRouter);

                    for (int i = 0; i < 2; i++)
                    {
                        var workerThread = new Thread((state) =>
                            {
                                byte[] routerId = (byte[])state;
                                byte[] workerId = Guid.NewGuid().ToByteArray();
                                using (var workerSocket = context.CreateDealerSocket())
                                {
                                    workerSocket.Options.Identity = workerId;
                                    workerSocket.Connect("inproc://backend");

                                    var workerReadyMsg = new NetMQMessage();
                                    workerReadyMsg.Append(workerId);
                                    workerReadyMsg.AppendEmptyFrame();
                                    workerReadyMsg.Append(s_ReadyMsg);
                                    workerSocket.SendMultipartMessage(workerReadyMsg);
                                    Thread.Sleep(1000);
                                }
                            });
                        workerThread.IsBackground = true;
                        workerThread.Name = "worker" + i;
                        workerThread.Start(backendsRouter.Options.Identity);
                        workers.Add(workerThread);
                    }

                    poller.PollTillCancelledNonBlocking();
                    Thread.Sleep(1000);
                    poller.CancelAndJoin();
                    Assert.AreEqual(2, s_FreeWorkers.Count);
                }
            }
        }

    }
}
