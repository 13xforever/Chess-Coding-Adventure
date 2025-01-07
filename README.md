# Chess-Coding-Adventure
Version 2.0 of the Coding Adventure Bot. Good at beating up humans (~2200 on [lichess](https://lichess.org/@/CodingAdventureBot/playing)), but still has a very long way to go against its fellow machines (Stockfish crushes it even with rook-odds!)

You can find some videos about the bot's creation process here: [V1](https://www.youtube.com/watch?v=U4ogK0MIzqk) and [V2](https://youtu.be/_vqlIPDR2TU)

Note: this is the UCI version of the program, which does not have a graphical interface. The UCI implementation is also very barebones – I just did the minimum to get it up and running on lichess.

# UCI Specification
A copy of specification is provided in [UCI Specification.md](UCI%20Specification.md), slightly updated to Markdown format and to fix some minor typos.

# Changes from upstream
* Upgraded solution to .NET 9
* Expanded UCI command implementation, including some basic options and engine info reporting
* Minor code fixes and optimizations