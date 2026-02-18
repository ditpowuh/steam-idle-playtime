# steam-idle-playtime

> A basic console application designed to passively increase Steam playtime with games of your choosing.

## Packages
- SteamKit2
- Sharprompt

## App Input
### Input Prompts
**Steam username** - This is the one you use to sign into Steam (not your profile name)
<br/>
**Steam password** - Input your Steam password
<br/>
**JSON File** - Input the path or file of a JSON file in the format below

If you have Steam Guard, you will be prompted to use the Steam Mobile App to confirm your sign in.

### JSON File
'target' and 'progress' are stored as minutes (e.g. a target of 60 means it'll run for an hour, before it goes to the next game). 'progress' should not be modified unless you intend to reset the count of minutes.
#### Example - Single Game
```json
[
  {
    "appID": 2215430,
    "target": 60,
    "progress": 0
  }
]
```

#### Example - Multiple Games in Queue
```json
[
  {
    "appID": 35140,
    "target": 120,
    "progress": 0
  },
  {
    "appID": 200260,
    "target": 120,
    "progress": 0
  },
  {
    "appID": 208650,
    "target": 120,
    "progress": 0
  }
]
```
