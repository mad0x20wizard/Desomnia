using MadWizard.Desomnia.Pipe;
using MadWizard.Desomnia.Pipe.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DesomniaSessionMinion.Test
{
    internal class MessagePipeClientTest
    {
        private static async Task Test()
        {
            var client = new MessagePipeClient(1);

            client.Connected += Client_Connected;
            client.MessageReceived += MessageReceived;
            client.Disconnected += Client_Disconnected;

            try
            {
                await client.Connect(new CancellationTokenSource(2500).Token);
            }
            catch (Exception)
            {
                Console.WriteLine("timeout");

                return;
            }

            //client.SendMessage(new StartupMessage());

            while (true)
            {
                Console.ReadLine();

                client.SendMessage(new InputTimeMessage(DateTime.Now));
            }
        }

        private static void Client_Disconnected(object sender, EventArgs e)
        {
            Console.WriteLine("disconnected");
        }

        private static void Client_Connected(object sender, EventArgs e)
        {
            Console.WriteLine("connected");
        }

        static void MessageReceived(object sender, MadWizard.Desomnia.Pipe.Messages.Message message)
        {
            Console.WriteLine($"Received: {message.GetType().Name}");
        }

    }
}
