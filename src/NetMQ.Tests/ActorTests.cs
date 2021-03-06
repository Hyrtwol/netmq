﻿using NUnit.Framework;

namespace NetMQ.Tests
{
    [TestFixture]
    public class ActorTests
    {
        [Test]
        public void Simple()
        {
            using (var context = NetMQContext.Create())
            {
                ShimAction shimAction = shim =>
                {
                    shim.SignalOK();

                    while (true)
                    {
                        NetMQMessage msg = shim.ReceiveMultipartMessage();

                        string command = msg[0].ConvertToString();

                        if (command == NetMQActor.EndShimMessage)
                            break;

                        if (command == "Hello")
                            shim.SendFrame("World");
                    }
                };

                using (var actor = NetMQActor.Create(context, shimAction))
                {
                    actor.SendMoreFrame("Hello").SendFrame("Hello");

                    Assert.AreEqual("World", actor.ReceiveFrameString());
                }
            }
        }
    }
}
