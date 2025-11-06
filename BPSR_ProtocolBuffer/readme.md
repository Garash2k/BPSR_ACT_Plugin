# What this

BPSR uses [Google's Protocol Buffer](https://protobuf.dev/) as part of the networking process for sending game data between the client and server.
This project contains a `.proto` file, which I made by reverse engineering [StarResonnanceDamageCounter's logic](https://github.com/dmlgzs/StarResonanceDamageCounter/tree/master/algo).
The proto file can be compiled into C# code using the `protoc` compiler.

# Why this a seperate project

ACT fails to load BPSR\_ACT\_Plugin with some weird error if we put the Protocol Buffer code in the main dll, so as a work around we can keep it in a seperate project and reference it from the main one.

# How do you build this

In this very folder, `protoc.exe BPSRProtocolBufferBareBones.proto --csharp_out .`
