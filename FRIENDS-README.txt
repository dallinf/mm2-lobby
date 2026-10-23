MM2 LOBBY - HOW TO JOIN A RACE
==============================

This program lets you race Midtown Madness 2 against your friends. It takes
the place of GameRanger. You do these steps one time. After that you only
start the program.

Before you start, the person who runs the group must write their network ID
in step 2 below. The ID has 16 characters.


WHAT YOU NEED
-------------

  1. Windows 10 or Windows 11.
  2. Your own copy of Midtown Madness 2 on this computer. You do not need to
     install the game. A folder that holds midtown2.exe is enough.
  3. The file Mm2Lobby.App.exe, from the person who runs the group.


STEP 1: INSTALL ZEROTIER
------------------------

ZeroTier joins your computer to the private network of the group. The race
needs it. Your computer and your friends then talk to each other as if they
sit in one room.

  1. Open https://www.zerotier.com/download/
  2. Download ZeroTier for Windows.
  3. Install it.

You do not need a ZeroTier account. You never type a password. You only need
the network ID in the next step.


STEP 2: JOIN THE NETWORK OF THE GROUP
-------------------------------------

The network ID of this group is:

    >>> THE HOST WRITES THE 16 CHARACTER ID HERE <<<

  1. Find the ZeroTier icon in the Windows system tray, near the clock.
  2. Right-click the icon.
  3. Choose Join Network.
  4. Type the ID above.
  5. Choose Join.


STEP 3: WAIT FOR THE HOST TO APPROVE YOU
----------------------------------------

Warning: you get no address until the host approves your computer. Nothing
works before that. This is normal, and it is not a fault of your computer.

  1. Tell the host that you joined the network.
  2. Wait. The host ticks a box for your computer in ZeroTier Central.
  3. The host tells you when the box is ticked.

The MM2 Lobby program also tells you. It shows this line while you wait:

    The owner of network ... has not approved this computer


STEP 4: START MM2 LOBBY
-----------------------

Warning: Windows blocks this program the first time. Nobody signed the
program, and it is 97 MB, so Windows shows a blue box. The box says "Windows
protected your PC", and it hides the Run button.

  1. Double-click Mm2Lobby.App.exe.
  2. Choose More info in the blue box.
  3. Choose Run anyway.

The program carries everything it needs. You install nothing else.


STEP 5: WORK DOWN THE SETUP PANEL
---------------------------------

The window shows a Setup panel with four rows. Each row names one problem and
offers a Fix button. Work down the rows in order. Each Fix asks Windows for
permission. Choose Yes each time.

  ZeroTier            Choose Fix. The Fix lets your own account read the
                      ZeroTier token file. Then type the network ID of the
                      group in the box called ZeroTier network ID, at the top
                      of the window. Click outside the box.

  DirectPlay          Choose Fix. DirectPlay is an old part of Windows, and
                      Midtown Madness 2 needs it. Windows may ask you to
                      restart the computer.

  Midtown Madness 2   Choose Fix. The program opens a file picker. Point the
                      picker at midtown2.exe in your game folder. The program
                      accepts that file name only.

  Windows Firewall    Choose Fix. The Fix adds two rules, so the firewall
                      stops no race. Windows asks for permission two times.

After each Fix, choose Check again. The Host button and the Join button stay
inactive until all four rows pass.

Your name shows at the top of the window. The program uses the name of your
computer. Type another name if you want one, then click outside the box.


STEP 6: JOIN A RACE
-------------------

  1. Wait for a friend to host a race. Their name shows in your window.
  2. Choose Join the host. The window shows the address of the host in large
     type. The program also copies the address.
  3. Choose Start the game. Midtown Madness 2 starts.
  4. In the game choose Multiplayer.
  5. Choose TCP/IP, then Join.
  6. Type the address of the host.

Read the address before step 3. The game covers the whole screen, so you
cannot read your MM2 Lobby window after the game starts. The game can also
refuse Ctrl+V, so you may have to type the address by hand.


IF SOMETHING GOES WRONG
-----------------------

Every row in the Setup panel names its own problem and says what to do. Read
the row first.

Tell the person who runs the group what the row says. The words matter. Two
rows can look alike and need different answers.
