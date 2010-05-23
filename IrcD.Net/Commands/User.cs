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

namespace IrcD.Commands
{
    public class User : CommandBase
    {
        public User(IrcDaemon ircDaemon)
            : base(ircDaemon, "USER")
        { }

        public override void Handle(UserInfo info, List<string> args)
        {

            if (!info.PassAccepted)
            {
                IrcDaemon.Replies.SendPasswordMismatch(info);
                return;
            }
            if (info.User != null)
            {
                IrcDaemon.Replies.SendAlreadyRegistered(info);
                return;
            }
            if (args.Count < 4)
            {
                IrcDaemon.Replies.SendNeedMoreParams(info);
                return;
            }

            int flags;
            int.TryParse(args[1], out flags);

            //TODO: new Modes
            //info.Mode_i = ((flags & 8) > 0);
            //info.Mode_w = ((flags & 4) > 0);

            info.User = args[0];
            info.Realname = args[3];

            if (info.Nick == null) return;

            info.Registered = true;
            IrcDaemon.Replies.RegisterComplete(info);
        }

        public override void Send(InfoBase receiver, object[] args)
        {
            receiver.WriteLine(Command);
            throw new NotImplementedException();
        }
    }
}
