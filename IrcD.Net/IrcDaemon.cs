﻿/*
 *  The ircd.net project is an IRC deamon implementation for the .NET Plattform
 *  It should run on both .NET and Mono
 * 
 *  Copyright (c) 2009-2010 Thomas Bruderer <apophis@apophis.ch>
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IrcD.Commands;
using IrcD.Modes;
using IrcD.Modes.ChannelModes;
using IrcD.Modes.ChannelRanks;
using IrcD.Modes.UserModes;
using IrcD.ServerReplies;
using IrcD.Utils;
using Mode = IrcD.Commands.Mode;
using Version = IrcD.Commands.Version;

namespace IrcD
{

    public class IrcDaemon
    {
        public const string ServerCrLf = "\r\n";
        const char PrefixCharacter = ':';
        const int MaxBufferSize = 2048;

        // Main Datastructures
        private readonly Dictionary<Socket, UserInfo> sockets = new Dictionary<Socket, UserInfo>();
        private readonly Dictionary<string, ChannelInfo> channels = new Dictionary<string, ChannelInfo>();
        private readonly Dictionary<string, Socket> nicks = new Dictionary<string, Socket>();
        public Dictionary<string, Socket> Nicks
        {
            get
            {
                return nicks;
            }
        }

        #region Modes
        private readonly RankList supportedRanks = new RankList();
        public RankList SupportedRanks
        {
            get
            {
                return supportedRanks;
            }
        }
        private readonly ChannelModeList supportedChannelModes = new ChannelModeList();
        public ChannelModeList SupportedChannelModes
        {
            get
            {
                return supportedChannelModes;
            }
        }
        private readonly UserModeList supportedUserModes = new UserModeList();
        public UserModeList SupportedUserModes
        {
            get
            {
                return supportedUserModes;
            }
        }
        #endregion

        // Protocol
        private readonly CommandList commands;
        public CommandList Commands
        {
            get
            {
                return commands;
            }
        }

        private readonly ProtocolMessages protocolMessages;
        public ProtocolMessages Send
        {
            get
            {
                return protocolMessages;
            }
        }

        private readonly ServerReplies.ServerReplies replies;
        public ServerReplies.ServerReplies Replies
        {
            get
            {
                return replies;
            }
        }

        private bool connected = true;
        private byte[] buffer = new byte[MaxBufferSize];
        private EndPoint ep = new IPEndPoint(0, 0);
        private EndPoint localEp;

        private readonly ServerOptions options = new ServerOptions();

        public ServerOptions Options
        {
            get
            {
                return options;
            }
        }

        public string ServerPrefix
        {
            get
            {
                if (string.IsNullOrEmpty(Options.ServerName))
                {
                    return PrefixCharacter + "ircd.net";
                }
                return PrefixCharacter + Options.ServerName;
            }
        }

        private readonly DateTime serverCreated;
        public DateTime ServerCreated
        {
            get
            {
                return serverCreated;
            }
        }

        public IrcDaemon()
        {
            // The Protocol Objects 
            commands = new CommandList(this);
            replies = new ServerReplies.ServerReplies(this);
            protocolMessages = new ProtocolMessages(this);
            serverCreated = DateTime.Now;

            // Add Commands
            AddCommands();
            // Add Modes
            AddModes();
        }

        private void AddCommands()
        {
            commands.Add(new Admin(this));
            commands.Add(new Away(this));
            commands.Add(new Connect(this));
            commands.Add(new Die(this));
            commands.Add(new Error(this));
            commands.Add(new Info(this));
            commands.Add(new Invite(this));
            commands.Add(new IsOn(this));
            commands.Add(new Join(this));
            commands.Add(new Kick(this));
            commands.Add(new Kill(this));
            commands.Add(new Knock(this));
            commands.Add(new Links(this));
            commands.Add(new List(this));
            commands.Add(new ListUsers(this));
            commands.Add(new MessageOfTheDay(this));
            commands.Add(new Mode(this));
            commands.Add(new Names(this));
            commands.Add(new Nick(this));
            commands.Add(new Notice(this));
            commands.Add(new Oper(this));
            commands.Add(new Part(this));
            commands.Add(new Pass(this));
            commands.Add(new Ping(this));
            commands.Add(new Pong(this));
            commands.Add(new PrivateMessage(this));
            commands.Add(new Quit(this));
            commands.Add(new Rehash(this));
            commands.Add(new Restart(this));
            commands.Add(new ServerQuit(this));
            commands.Add(new Service(this));
            commands.Add(new ServiceList(this));
            commands.Add(new ServiceQuery(this));
            commands.Add(new Stats(this));
            commands.Add(new Summon(this));
            commands.Add(new Time(this));
            commands.Add(new Topic(this));
            commands.Add(new Trace(this));
            commands.Add(new User(this));
            commands.Add(new UserHost(this));
            commands.Add(new Version(this));
            commands.Add(new Wallops(this));
            commands.Add(new Who(this));
            commands.Add(new WhoIs(this));
            commands.Add(new WhoWas(this));

        }

        private void AddModes()
        {
            supportedRanks.Add(new ModeVoice());
            supportedRanks.Add(new ModeHalfOp());
            supportedRanks.Add(new ModeOp());

            supportedChannelModes.Add(new ModeBan());
            supportedChannelModes.Add(new ModeBanException());
            supportedChannelModes.Add(new ModeColorless());
            supportedChannelModes.Add(new ModeKey());
            supportedChannelModes.Add(new ModeLimit());
            supportedChannelModes.Add(new ModeModerated());
            supportedChannelModes.Add(new ModeNoExternal());
            supportedChannelModes.Add(new ModePrivate());

            supportedUserModes.Add(new ModeInvisible());
            supportedUserModes.Add(new ModeRestricted());
        }

        public void MainLoop()
        {

            localEp = new IPEndPoint(IPAddress.Any, Options.ServerPort);
            var connectSocket = new Socket(localEp.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            connectSocket.Bind(localEp);
            connectSocket.Listen(20);

            sockets.Add(connectSocket, new UserInfo(this, connectSocket, "TODO:Server", true, true));

            while (connected)
            {
                var activeSockets = new List<Socket>(sockets.Keys);

                Socket.Select(activeSockets, null, null, 2000000);

                foreach (Socket s in activeSockets)
                {
                    try
                    {
                        if (sockets[s].IsAcceptSocket)
                        {
                            Socket temp = s.Accept();
                            sockets.Add(temp, new UserInfo(this, temp, ((IPEndPoint)temp.RemoteEndPoint).Address.ToString(), false, String.IsNullOrEmpty(Options.ServerPass)));
                            Logger.Log("Client connected!");
                        }
                        else
                        {
                            try
                            {
                                buffer.Initialize();
                                int numBytes = s.ReceiveFrom(buffer, ref ep);
                                foreach (string line in Encoding.UTF8.GetString(buffer, 0, numBytes).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    Parser(line, s, sockets[s]);
                                }
                            }
                            catch (SocketException e)
                            {
                                Logger.Log("ERROR: " + e.Message + "(CODE:" + e.ErrorCode + ")");
                                //Send.Quit(sockets[s], new List<string> { "Socket reset by peer" });
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log("Unknown ERROR: " + e.Message);
                        Logger.Log("Trace: " + e.StackTrace);
                    }
                }

                // Pinger : we only ping if necessary
                foreach (var user in from user in sockets.Where(s => s.Value.Registered)
                                     let interval = DateTime.Now.AddMinutes(-1)
                                     where user.Value.LastAction < interval && user.Value.LastAlive < interval
                                     select user.Value)
                {
                    if (user.LastAlive < DateTime.Now.AddMinutes(-5))
                    {
                        // Ping Timeout (5 Minutes without any life sign)
                        user.Remove();
                    }
                    else
                    {
                        Send.Ping(user);
                    }
                }

            }
        }

        public void Parser(string line, Socket sock, UserInfo info)
        {

#if DEBUG
            Logger.Log(line, location: "IN:");
#endif

            string prefix = null;
            string command = null;
            var replycode = ReplyCode.Null;
            var args = new List<string>();

            try
            {
                int i = 0;
                /* This runs in the mainloop :: parser needs to return fast
                 * -> nothing which could block it may be called inside Parser
                 * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * * */
                if (line[0] == PrefixCharacter)
                {
                    /* we have a prefix */
                    while (line[++i] != ' ') { }
                    prefix = line.Substring(1, i - 1);
                }
                else
                {
                    prefix = info.Usermask;
                }

                int commandStart = i;
                /*command might be numeric (xxx) or command */
                if (char.IsDigit(line[i + 1]) && char.IsDigit(line[i + 2]) && char.IsDigit(line[i + 3]))
                {
                    replycode = (ReplyCode)int.Parse(line.Substring(i + 1, 3));
                    i += 4;
                }
                else
                {
                    while ((i < (line.Length - 1)) && line[++i] != ' ') { }
                    if (line.Length - 1 == i) { ++i; }
                    command = line.Substring(commandStart, i - commandStart);
                }

                ++i;
                int paramStart = i;
                while (i < line.Length)
                {
                    if (line[i] == ' ' && i != paramStart)
                    {
                        args.Add(line.Substring(paramStart, i - paramStart));
                        paramStart = i + 1;
                    }
                    if (line[i] == PrefixCharacter)
                    {
                        if (paramStart != i)
                        {
                            args.Add(line.Substring(paramStart, i - paramStart));
                        }
                        args.Add(line.Substring(i + 1));
                        break;
                    }
                    ++i;
                }

                if (i == line.Length)
                {
                    args.Add(line.Substring(paramStart));
                }

            }
            catch (IndexOutOfRangeException)
            {
                // invalid message
            }

            if (command == null)
                return;

            commands.Handle(command, info, args);
        }

        #region Helper Methods

        private string[] GetSubArgument(string arg)
        {
            return arg.Split(new[] { ',' });
        }


        /// <summary>
        /// Check if an IRC Operatur status can be granted upon user and pass
        /// </summary>
        /// <param name="user"></param>
        /// <param name="pass"></param>
        /// <returns></returns>
        private bool IsIrcOp(string user, string pass)
        {
            string realpass;
            if (Options.OLine.TryGetValue(user, out realpass))
            {
                if (pass == realpass)
                    return true;
            }
            return false;
        }
        #endregion

    }
}
