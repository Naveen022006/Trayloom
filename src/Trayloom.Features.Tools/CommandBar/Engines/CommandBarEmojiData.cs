// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

namespace Trayloom.Features.Tools;

/// <summary>
/// Curated emoji table (single-scalar emoji only). One entry per line: the glyph (without variation selector), a space and a
/// lower-case Unicode-style name. Text-default scalars receive U+FE0F when the table is loaded.
/// </summary>
internal static class CommandBarEmojiData
{
    /// <summary>The ~216 popular emoji in their fixed display order (one scalar each).</summary>
    public const string Popular =
        "😀😂🤣😊😍🥰😘😎🤔🙄😭🥺😡🤬😱😴🤯😇" +
        "😉😅😆🙂🙃😋😜🤪🤗🤩😏😬😢😤🥳😷🤒🤢" +
        "👍👎👌✌🤞🤝🙏👏🙌🫶💪👋🤷🤦👀🧠👈👉" +
        "❤🧡💛💚💙💜🖤🤍💔💕💖💗💯💥✨🔥⭐🌟" +
        "✅❌⚠❓❗💡🎉🎊🚀🏆🎯💎🔔⏰📌🔒🔑💀" +
        "☀🌙⛅☁🌧⚡❄🌈☔🍕🍔🍟🍎🍌🍰🍺☕🍻" +
        "🚗🚕🚌🚲✈🏠🏢🗺🌍🌎🏖⛰🚢🚂🚁🏁🚩⛽" +
        "💻🖥📱⌨🖱📷🎥📺🎧🎤🔋🔌💾📡🔍🔬🔭🧪" +
        "📁📂📄📝📋📅📆📎✂🔧🔨⚙🧰📦✉📧💼🗑" +
        "🐶🐱🐭🦊🐻🐼🐯🦁🐮🐷🐸🐵🐔🐧🦄🐝🦋🐢" +
        "🌸🌹🌻🌷🌱🌲🌳🌴🌵🍀🍁🍄🍇🍓🍉🍊🍋🥑" +
        "⌚⏳⏱📈📉📊🥇🥈🥉🏅🎁🎈🎂💰💳🧾📣";

    /// <summary>The full table.</summary>
    public const string Table = """
😀 grinning face
😁 grinning face with smiling eyes
😂 face with tears of joy
🤣 rolling on the floor laughing
😃 smiling face with open mouth
😄 smiling face with open mouth and smiling eyes
😅 smiling face with open mouth and cold sweat
😆 smiling face with open mouth and tightly-closed eyes
😉 winking face
😊 smiling face with smiling eyes
😋 face savouring delicious food
😎 smiling face with sunglasses
😍 smiling face with heart-shaped eyes
😘 face throwing a kiss
🥰 smiling face with smiling eyes and three hearts
😗 kissing face
😙 kissing face with smiling eyes
😚 kissing face with closed eyes
☺ white smiling face
🙂 slightly smiling face
🤗 hugging face
🤩 grinning face with star eyes
🤔 thinking face
🤨 face with one eyebrow raised
😐 neutral face
😑 expressionless face
😶 face without mouth
🙄 face with rolling eyes
😏 smirking face
😣 persevering face
😥 disappointed but relieved face
😮 face with open mouth
🤐 zipper-mouth face
😯 hushed face
😪 sleepy face
😫 tired face
😴 sleeping face
😌 relieved face
😛 face with stuck-out tongue
😜 face with stuck-out tongue and winking eye
😝 face with stuck-out tongue and tightly-closed eyes
🤤 drooling face
😒 unamused face
😓 face with cold sweat
😔 pensive face
😕 confused face
🙃 upside-down face
🤑 money-mouth face
😲 astonished face
☹ white frowning face
🙁 slightly frowning face
😖 confounded face
😞 disappointed face
😟 worried face
😤 face with look of triumph
😢 crying face
😭 loudly crying face
😦 frowning face with open mouth
😧 anguished face
😨 fearful face
😩 weary face
🤯 shocked face with exploding head
😬 grimacing face
😰 face with open mouth and cold sweat
😱 face screaming in fear
🥵 overheated face
🥶 freezing face
😳 flushed face
🤪 grinning face with one large and one small eye
😵 dizzy face
😡 pouting face
😠 angry face
🤬 serious face with symbols covering mouth
😷 face with medical mask
🤒 face with thermometer
🤕 face with head-bandage
🤢 nauseated face
🤮 face with open mouth vomiting
🤧 sneezing face
😇 smiling face with halo
🤠 face with cowboy hat
🥳 face with party horn and party hat
🥴 face with uneven eyes and wavy mouth
🥺 face with pleading eyes
🤥 lying face
🤫 face with finger covering closed lips
🤭 face with hand over mouth
🧐 face with monocle
🤓 nerd face
🤡 clown face
🥱 yawning face
🥲 smiling face with tear
🥹 face holding back tears
🫠 melting face
🫡 saluting face
🫢 face with open eyes and hand over mouth
🫣 face with peeking eye
🫤 face with diagonal mouth
🫥 dotted line face
🫨 shaking face
🥸 disguised face
😈 smiling face with horns
👿 imp
👹 japanese ogre
👺 japanese goblin
💀 skull
☠ skull and crossbones
👻 ghost
👽 extraterrestrial alien
👾 alien monster
🤖 robot face
💩 pile of poo
😺 smiling cat face with open mouth
😸 grinning cat face with smiling eyes
😹 cat face with tears of joy
😻 smiling cat face with heart-shaped eyes
😼 cat face with wry smile
😽 kissing cat face with closed eyes
🙀 weary cat face
😿 crying cat face
😾 pouting cat face
🙈 see-no-evil monkey
🙉 hear-no-evil monkey
🙊 speak-no-evil monkey
👋 waving hand sign
🤚 raised back of hand
🖐 raised hand with fingers splayed
✋ raised hand
🖖 raised hand with part between middle and ring fingers
👌 ok hand sign
🤌 pinched fingers
🤏 pinching hand
✌ victory hand
🤞 hand with index and middle fingers crossed
🤟 i love you hand sign
🤘 sign of the horns
🤙 call me hand
👈 white left pointing backhand index
👉 white right pointing backhand index
👆 white up pointing backhand index
🖕 reversed hand with middle finger extended
👇 white down pointing backhand index
☝ white up pointing index
👍 thumbs up sign
👎 thumbs down sign
✊ raised fist
👊 fisted hand sign
🤛 left-facing fist
🤜 right-facing fist
👏 clapping hands sign
🙌 person raising both hands in celebration
👐 open hands sign
🤲 palms up together
🤝 handshake
🙏 person with folded hands
✍ writing hand
💅 nail polish
🤳 selfie
💪 flexed biceps
🦾 mechanical arm
🦿 mechanical leg
🦵 leg
🦶 foot
👂 ear
🦻 ear with hearing aid
👃 nose
🧠 brain
🫀 anatomical heart
🫁 lungs
🦷 tooth
🦴 bone
👀 eyes
👁 eye
👅 tongue
👄 mouth
💋 kiss mark
🩸 drop of blood
🫶 heart hands
🫰 hand with index finger and thumb crossed
🫱 rightwards hand
🫲 leftwards hand
🫳 palm down hand
🫴 palm up hand
🫵 index pointing at the viewer
🫷 leftwards pushing hand
🫸 rightwards pushing hand
👶 baby
🧒 child
👦 boy
👧 girl
🧑 adult
👱 person with blond hair
👨 man
🧔 bearded person
👩 woman
🧓 older adult
👴 older man
👵 older woman
🙍 person frowning
🙎 person with pouting face
🙅 face with no good gesture
🙆 face with ok gesture
💁 information desk person
🙋 happy person raising one hand
🧏 deaf person
🙇 person bowing deeply
🤦 face palm
🤷 shrug
👮 police officer
🕵 sleuth or spy
💂 guardsman
🥷 ninja
👷 construction worker
🤴 prince
👸 princess
👳 man with turban
👲 man with gua pi mao
🧕 person with headscarf
🤵 man in tuxedo
👰 bride with veil
🤰 pregnant woman
🤱 breast-feeding
👼 baby angel
🎅 father christmas
🤶 mother christmas
🦸 superhero
🦹 supervillain
🧙 mage
🧚 fairy
🧛 vampire
🧜 merperson
🧝 elf
🧞 genie
🧟 zombie
💆 face massage
💇 haircut
🚶 pedestrian
🧍 standing person
🧎 kneeling person
🏃 runner
💃 dancer
🕺 man dancing
🕴 man in business suit levitating
👯 woman with bunny ears
🧖 person in steamy room
🧗 person climbing
🏇 horse racing
⛷ skier
🏂 snowboarder
🏌 golfer
🏄 surfer
🚣 rowboat
🏊 swimmer
⛹ person with ball
🏋 weight lifter
🚴 bicyclist
🚵 mountain bicyclist
🤸 person doing cartwheel
🤼 wrestlers
🤽 water polo
🤾 handball
🤹 juggling
🧘 person in lotus position
🛀 bath
🛌 sleeping accommodation
👪 family
👫 man and woman holding hands
👬 two men holding hands
👭 two women holding hands
❤ heavy black heart
🧡 orange heart
💛 yellow heart
💚 green heart
💙 blue heart
💜 purple heart
🖤 black heart
🤍 white heart
🤎 brown heart
💔 broken heart
❣ heavy heart exclamation mark ornament
💕 two hearts
💞 revolving hearts
💓 beating heart
💗 growing heart
💖 sparkling heart
💘 heart with arrow
💝 heart with ribbon
💟 heart decoration
💌 love letter
💤 sleeping symbol
💢 anger symbol
💣 bomb
💥 collision symbol
💦 splashing sweat symbol
💨 dash symbol
💫 dizzy symbol
💬 speech balloon
🗨 left speech bubble
🗯 right anger bubble
💭 thought balloon
🕳 hole
☀ black sun with rays
🌤 white sun with small cloud
⛅ sun behind cloud
🌥 white sun behind cloud
☁ cloud
🌦 white sun behind cloud with rain
🌧 cloud with rain
⛈ thunder cloud and rain
🌩 cloud with lightning
🌨 cloud with snow
❄ snowflake
☃ snowman
⛄ snowman without snow
🌬 wind blowing face
🌪 cloud with tornado
🌫 fog
🌈 rainbow
☔ umbrella with rain drops
☂ umbrella
⚡ high voltage sign
🔥 fire
💧 droplet
🌊 water wave
🌙 crescent moon
🌛 first quarter moon with face
🌜 last quarter moon with face
🌝 full moon with face
🌞 sun with face
🌚 new moon with face
🌑 new moon symbol
🌒 waxing crescent moon symbol
🌓 first quarter moon symbol
🌔 waxing gibbous moon symbol
🌕 full moon symbol
🌖 waning gibbous moon symbol
🌗 last quarter moon symbol
🌘 waning crescent moon symbol
⭐ white medium star
🌟 glowing star
✨ sparkles
🌠 shooting star
🌌 milky way
☄ comet
🪐 ringed planet
🌍 earth globe europe-africa
🌎 earth globe americas
🌏 earth globe asia-australia
🌐 globe with meridians
🌋 volcano
🗻 mount fuji
🏔 snow capped mountain
⛰ mountain
🐶 dog face
🐱 cat face
🐭 mouse face
🐹 hamster face
🐰 rabbit face
🦊 fox face
🐻 bear face
🐼 panda face
🐨 koala
🐯 tiger face
🦁 lion face
🐮 cow face
🐷 pig face
🐽 pig nose
🐸 frog face
🐵 monkey face
🐒 monkey
🦍 gorilla
🦧 orangutan
🐔 chicken
🐧 penguin
🐦 bird
🐤 baby chick
🐣 hatching chick
🐥 front-facing baby chick
🦆 duck
🦅 eagle
🦉 owl
🦇 bat
🐺 wolf face
🐗 boar
🐴 horse face
🦄 unicorn face
🐝 honeybee
🐛 bug
🦋 butterfly
🐌 snail
🐞 lady beetle
🐜 ant
🦟 mosquito
🦗 cricket
🕷 spider
🕸 spider web
🦂 scorpion
🐢 turtle
🐍 snake
🦎 lizard
🦖 t-rex
🦕 sauropod
🐙 octopus
🦑 squid
🦐 shrimp
🦞 lobster
🦀 crab
🐡 blowfish
🐠 tropical fish
🐟 fish
🐬 dolphin
🐳 spouting whale
🐋 whale
🦈 shark
🐊 crocodile
🐅 tiger
🐆 leopard
🦓 zebra
🐘 elephant
🦏 rhinoceros
🦛 hippopotamus
🐪 dromedary camel
🐫 bactrian camel
🦒 giraffe
🦘 kangaroo
🐃 water buffalo
🐂 ox
🐄 cow
🐎 horse
🐖 pig
🐏 ram
🐑 sheep
🦙 llama
🐐 goat
🦌 deer
🐕 dog
🐩 poodle
🐈 cat
🐓 rooster
🦃 turkey
🦚 peacock
🦜 parrot
🦢 swan
🕊 dove of peace
🐇 rabbit
🦝 raccoon
🦨 skunk
🦡 badger
🦦 otter
🦥 sloth
🐁 mouse
🐀 rat
🐿 chipmunk
🦔 hedgehog
🐾 paw prints
🐉 dragon
🐲 dragon face
🦭 seal
🪱 worm
🪲 beetle
🪳 cockroach
🪰 fly
🦬 bison
🦣 mammoth
🦫 beaver
💐 bouquet
🌸 cherry blossom
💮 white flower
🏵 rosette
🌹 rose
🥀 wilted flower
🌺 hibiscus
🌻 sunflower
🌼 blossom
🌷 tulip
🌱 seedling
🌲 evergreen tree
🌳 deciduous tree
🌴 palm tree
🌵 cactus
🌾 ear of rice
🌿 herb
☘ shamrock
🍀 four leaf clover
🍁 maple leaf
🍂 fallen leaf
🍃 leaf fluttering in wind
🍄 mushroom
🪴 potted plant
🪨 rock
🪵 wood
🍇 grapes
🍈 melon
🍉 watermelon
🍊 tangerine
🍋 lemon
🍌 banana
🍍 pineapple
🥭 mango
🍎 red apple
🍏 green apple
🍐 pear
🍑 peach
🍒 cherries
🍓 strawberry
🫐 blueberries
🥝 kiwifruit
🍅 tomato
🫒 olive
🥥 coconut
🥑 avocado
🍆 aubergine
🥔 potato
🥕 carrot
🌽 ear of maize
🌶 hot pepper
🫑 bell pepper
🥒 cucumber
🥬 leafy green
🥦 broccoli
🧄 garlic
🧅 onion
🥜 peanuts
🌰 chestnut
🍞 bread
🥐 croissant
🥖 baguette bread
🫓 flatbread
🥨 pretzel
🥯 bagel
🥞 pancakes
🧇 waffle
🧀 cheese wedge
🍖 meat on bone
🍗 poultry leg
🥩 cut of meat
🥓 bacon
🍔 hamburger
🍟 french fries
🍕 slice of pizza
🌭 hot dog
🥪 sandwich
🌮 taco
🌯 burrito
🫔 tamale
🥙 stuffed flatbread
🧆 falafel
🥚 egg
🍳 cooking
🥘 shallow pan of food
🍲 pot of food
🫕 fondue
🥣 bowl with spoon
🥗 green salad
🍿 popcorn
🧈 butter
🧂 salt shaker
🥫 canned food
🍱 bento box
🍘 rice cracker
🍙 rice ball
🍚 cooked rice
🍛 curry and rice
🍜 steaming bowl
🍝 spaghetti
🍠 roasted sweet potato
🍢 oden
🍣 sushi
🍤 fried shrimp
🍥 fish cake with swirl design
🥮 moon cake
🍡 dango
🥟 dumpling
🥠 fortune cookie
🥡 takeout box
🍦 soft ice cream
🍧 shaved ice
🍨 ice cream
🍩 doughnut
🍪 cookie
🎂 birthday cake
🍰 shortcake
🧁 cupcake
🥧 pie
🍫 chocolate bar
🍬 candy
🍭 lollipop
🍮 custard
🍯 honey pot
🍼 baby bottle
🥛 glass of milk
☕ hot beverage
🫖 teapot
🍵 teacup without handle
🍶 sake bottle and cup
🍾 bottle with popping cork
🍷 wine glass
🍸 cocktail glass
🍹 tropical drink
🍺 beer mug
🍻 clinking beer mugs
🥂 clinking glasses
🥃 tumbler glass
🥤 cup with straw
🧋 bubble tea
🧃 beverage box
🧉 mate drink
🧊 ice cube
🥢 chopsticks
🍽 fork and knife with plate
🍴 fork and knife
🥄 spoon
🔪 hocho
🏺 amphora
🚗 automobile
🚕 taxi
🚙 recreational vehicle
🚌 bus
🚎 trolleybus
🏎 racing car
🚓 police car
🚑 ambulance
🚒 fire engine
🚐 minibus
🚚 delivery truck
🚛 articulated lorry
🚜 tractor
🏍 racing motorcycle
🛵 motor scooter
🚲 bicycle
🛴 scooter
🛹 skateboard
🚨 police cars revolving light
🚔 oncoming police car
🚍 oncoming bus
🚘 oncoming automobile
🚖 oncoming taxi
🚡 aerial tramway
🚠 mountain cableway
🚟 suspension railway
🚃 railway car
🚋 tram car
🚞 mountain railway
🚝 monorail
🚄 high-speed train
🚅 high-speed train with bullet nose
🚈 light rail
🚂 steam locomotive
🚆 train
🚇 metro
🚊 tram
🚉 station
✈ airplane
🛫 airplane departure
🛬 airplane arriving
🛩 small airplane
💺 seat
🚁 helicopter
🚀 rocket
🛸 flying saucer
🛰 satellite
🚢 ship
⛵ sailboat
🚤 speedboat
🛥 motor boat
🛳 passenger ship
⛴ ferry
⚓ anchor
⛽ fuel pump
🚧 construction sign
🚦 vertical traffic light
🚥 horizontal traffic light
🚏 bus stop
🗺 world map
🗿 moyai
🗽 statue of liberty
🗼 tokyo tower
🏰 european castle
🏯 japanese castle
🏟 stadium
🎡 ferris wheel
🎢 roller coaster
🎠 carousel horse
⛲ fountain
⛱ umbrella on ground
🏖 beach with umbrella
🏝 desert island
🏜 desert
⛺ tent
🏠 house building
🏡 house with garden
🏘 house buildings
🏚 derelict house building
🏗 building construction
🏭 factory
🏢 office building
🏬 department store
🏣 japanese post office
🏤 european post office
🏥 hospital
🏦 bank
🏨 hotel
🏪 convenience store
🏫 school
🏩 love hotel
💒 wedding
🏛 classical building
⛪ church
🕌 mosque
🛕 hindu temple
🕍 synagogue
⛩ shinto shrine
🌃 night with stars
🏙 cityscape
🌄 sunrise over mountains
🌅 sunrise
🌆 cityscape at dusk
🌇 sunset over buildings
🌉 bridge at night
⚽ soccer ball
🏀 basketball and hoop
🏈 american football
⚾ baseball
🥎 softball
🎾 tennis racquet and ball
🏐 volleyball
🏉 rugby football
🥏 flying disc
🎱 billiards
🏓 table tennis paddle and ball
🏸 badminton racquet and shuttlecock
🏒 ice hockey stick and puck
🏑 field hockey stick and ball
🥍 lacrosse stick and ball
🏏 cricket bat and ball
🥅 goal net
⛳ flag in hole
🏹 bow and arrow
🎣 fishing pole and fish
🤿 diving mask
🥊 boxing glove
🥋 martial arts uniform
🎽 running shirt with sash
⛸ ice skate
🥌 curling stone
🛷 sled
🎿 ski and ski boot
🎯 direct hit
🪀 yo-yo
🪁 kite
🔮 crystal ball
🧿 nazar amulet
🎮 video game
🕹 joystick
🎰 slot machine
🎲 game die
🧩 jigsaw puzzle piece
🧸 teddy bear
♠ black spade suit
♥ black heart suit
♦ black diamond suit
♣ black club suit
♟ black chess pawn
🃏 playing card black joker
🀄 mahjong tile red dragon
🎴 flower playing cards
🎭 performing arts
🖼 frame with picture
🎨 artist palette
🧵 spool of thread
🧶 ball of yarn
🎃 jack-o-lantern
🎄 christmas tree
🎆 fireworks
🎇 firework sparkler
🧨 firecracker
🎈 balloon
🎉 party popper
🎊 confetti ball
🎋 tanabata tree
🎍 pine decoration
🎎 japanese dolls
🎏 carp streamer
🎐 wind chime
🎑 moon viewing ceremony
🧧 red gift envelope
🎀 ribbon
🎁 wrapped present
🎗 reminder ribbon
🎟 admission tickets
🎫 ticket
🎖 military medal
🏆 trophy
🏅 sports medal
🥇 first place medal
🥈 second place medal
🥉 third place medal
👓 eyeglasses
🕶 dark sunglasses
🥽 goggles
🥼 lab coat
🦺 safety vest
👔 necktie
👕 t-shirt
👖 jeans
🧣 scarf
🧤 gloves
🧥 coat
🧦 socks
👗 dress
👘 kimono
🥻 sari
🩱 one-piece swimsuit
🩲 briefs
🩳 shorts
👙 bikini
👚 womans clothes
👛 purse
👜 handbag
👝 pouch
🛍 shopping bags
🎒 school satchel
🩴 thong sandal
👞 mans shoe
👟 athletic shoe
🥾 hiking boot
🥿 flat shoe
👠 high-heeled shoe
👡 womans sandal
🩰 ballet shoes
👢 womans boots
👑 crown
👒 womans hat
🎩 top hat
🎓 graduation cap
🧢 billed cap
⛑ helmet with white cross
📿 prayer beads
💄 lipstick
💍 ring
💎 gem stone
🔇 speaker with cancellation stroke
🔈 speaker
🔉 speaker with one sound wave
🔊 speaker with three sound waves
📢 public address loudspeaker
📣 cheering megaphone
📯 postal horn
🔔 bell
🔕 bell with cancellation stroke
🎼 musical score
🎵 musical note
🎶 multiple musical notes
🎙 studio microphone
🎚 level slider
🎛 control knobs
🎤 microphone
🎧 headphone
📻 radio
🎷 saxophone
🎸 guitar
🎹 musical keyboard
🎺 trumpet
🎻 violin
🥁 drum with drumsticks
📱 mobile phone
📲 mobile phone with rightwards arrow at left
☎ black telephone
📞 telephone receiver
📟 pager
📠 fax machine
🔋 battery
🔌 electric plug
💻 personal computer
🖥 desktop computer
🖨 printer
⌨ keyboard
🖱 three button mouse
🖲 trackball
💽 minidisc
💾 floppy disk
💿 optical disc
📀 dvd
🧮 abacus
🎥 movie camera
🎞 film frames
📽 film projector
🎬 clapper board
📺 television
📷 camera
📸 camera with flash
📹 video camera
📼 videocassette
🔍 left-pointing magnifying glass
🔎 right-pointing magnifying glass
🕯 candle
💡 electric light bulb
🔦 electric torch
🏮 izakaya lantern
📔 notebook with decorative cover
📕 closed book
📖 open book
📗 green book
📘 blue book
📙 orange book
📚 books
📓 notebook
📒 ledger
📃 page with curl
📜 scroll
📄 page facing up
📰 newspaper
🗞 rolled-up newspaper
📑 bookmark tabs
🔖 bookmark
🏷 label
💰 money bag
💴 banknote with yen sign
💵 banknote with dollar sign
💶 banknote with euro sign
💷 banknote with pound sign
💸 money with wings
💳 credit card
🧾 receipt
💹 chart with upwards trend and yen sign
✉ envelope
📧 e-mail symbol
📨 incoming envelope
📩 envelope with downwards arrow above
📤 outbox tray
📥 inbox tray
📦 package
📫 closed mailbox with raised flag
📪 closed mailbox with lowered flag
📬 open mailbox with raised flag
📭 open mailbox with lowered flag
📮 postbox
🗳 ballot box with ballot
✏ pencil
✒ black nib
🖋 lower left fountain pen
🖊 lower left ballpoint pen
🖌 lower left paintbrush
🖍 lower left crayon
📝 memo
💼 briefcase
📁 file folder
📂 open file folder
🗂 card index dividers
📅 calendar
📆 tear-off calendar
🗒 spiral note pad
🗓 spiral calendar pad
📇 card index
📈 chart with upwards trend
📉 chart with downwards trend
📊 bar chart
📋 clipboard
📌 pushpin
📍 round pushpin
📎 paperclip
🖇 linked paperclips
📏 straight ruler
📐 triangular ruler
✂ black scissors
🗃 card file box
🗄 file cabinet
🗑 wastebasket
🔒 lock
🔓 open lock
🔏 lock with ink pen
🔐 closed lock with key
🔑 key
🗝 old key
🔨 hammer
🪓 axe
⛏ pick
⚒ hammer and pick
🛠 hammer and wrench
🗡 dagger knife
⚔ crossed swords
🔫 pistol
🪃 boomerang
🛡 shield
🔧 wrench
🔩 nut and bolt
⚙ gear
🗜 compression
⚖ scales
🦯 white cane
🔗 link symbol
⛓ chains
🪝 hook
🧰 toolbox
🧲 magnet
🪜 ladder
⚗ alembic
🧪 test tube
🧫 petri dish
🧬 dna double helix
🔬 microscope
🔭 telescope
📡 satellite antenna
💉 syringe
💊 pill
🩹 adhesive bandage
🩺 stethoscope
🚪 door
🛏 bed
🛋 couch and lamp
🪑 chair
🚽 toilet
🚿 shower
🛁 bathtub
🪒 razor
🧴 lotion bottle
🧷 safety pin
🧹 broom
🧺 basket
🧻 roll of paper
🧼 bar of soap
🧽 sponge
🧯 fire extinguisher
🛒 shopping trolley
🚬 smoking symbol
⚰ coffin
⚱ funeral urn
🪦 headstone
⌚ watch
⏰ alarm clock
⏱ stopwatch
⏲ timer clock
🕰 mantelpiece clock
⌛ hourglass
⏳ hourglass with flowing sand
🕐 clock face one oclock
🕑 clock face two oclock
🕒 clock face three oclock
🕓 clock face four oclock
🕔 clock face five oclock
🕕 clock face six oclock
🕖 clock face seven oclock
🕗 clock face eight oclock
🕘 clock face nine oclock
🕙 clock face ten oclock
🕚 clock face eleven oclock
🕛 clock face twelve oclock
✅ white heavy check mark
❌ cross mark
❎ negative squared cross mark
✔ heavy check mark
✖ heavy multiplication x
➕ heavy plus sign
➖ heavy minus sign
➗ heavy division sign
❓ black question mark ornament
❔ white question mark ornament
❕ white exclamation mark ornament
❗ heavy exclamation mark symbol
‼ double exclamation mark
⁉ exclamation question mark
⚠ warning sign
🚸 children crossing
⛔ no entry
🚫 no entry sign
🚳 no bicycles
🚭 no smoking symbol
🚯 do not litter symbol
🚱 non-potable water symbol
🚷 no pedestrians
📵 no mobile phones
🔞 no one under eighteen symbol
☢ radioactive sign
☣ biohazard sign
⬆ upwards black arrow
↗ north east arrow
➡ black rightwards arrow
↘ south east arrow
⬇ downwards black arrow
↙ south west arrow
⬅ leftwards black arrow
↖ north west arrow
↕ up down arrow
↔ left right arrow
↩ leftwards arrow with hook
↪ rightwards arrow with hook
⤴ arrow pointing rightwards then curving upwards
⤵ arrow pointing rightwards then curving downwards
🔃 clockwise downwards and upwards open circle arrows
🔄 anticlockwise downwards and upwards open circle arrows
🔙 back with leftwards arrow above
🔚 end with leftwards arrow above
🔛 on with exclamation mark with left right arrow above
🔜 soon with rightwards arrow above
🔝 top with upwards arrow above
🛐 place of worship
⚛ atom symbol
🕉 om symbol
✡ star of david
☸ wheel of dharma
☯ yin yang
✝ latin cross
☦ orthodox cross
☪ star and crescent
☮ peace symbol
🕎 menorah with nine branches
🔯 six pointed star with middle dot
♈ aries
♉ taurus
♊ gemini
♋ cancer
♌ leo
♍ virgo
♎ libra
♏ scorpius
♐ sagittarius
♑ capricorn
♒ aquarius
♓ pisces
⛎ ophiuchus
🔀 twisted rightwards arrows
🔁 clockwise rightwards and leftwards open circle arrows
🔂 clockwise rightwards and leftwards open circle arrows with circled one overlay
▶ black right-pointing triangle
⏩ black right-pointing double triangle
⏭ black right-pointing double triangle with vertical bar
⏯ black right-pointing triangle with double vertical bar
◀ black left-pointing triangle
⏪ black left-pointing double triangle
⏮ black left-pointing double triangle with vertical bar
🔼 up-pointing small red triangle
⏫ black up-pointing double triangle
🔽 down-pointing small red triangle
⏬ black down-pointing double triangle
⏸ double vertical bar
⏹ black square for stop
⏺ black circle for record
⏏ eject symbol
🎦 cinema
🔅 low brightness symbol
🔆 high brightness symbol
📶 antenna with bars
📳 vibration mode
📴 mobile phone off
♀ female sign
♂ male sign
⚧ transgender symbol
♾ permanent paper sign
♻ black universal recycling symbol
⚜ fleur-de-lis
🔱 trident emblem
📛 name badge
🔰 japanese symbol for beginner
⭕ heavy large circle
💯 hundred points symbol
🔟 keycap ten
🔠 input symbol for latin capital letters
🔡 input symbol for latin small letters
🔢 input symbol for numbers
🔣 input symbol for symbols
🔤 input symbol for latin letters
🅰 negative squared latin capital letter a
🆎 negative squared ab
🅱 negative squared latin capital letter b
🆑 squared cl
🆒 squared cool
🆓 squared free
ℹ information source
🆔 squared id
Ⓜ circled latin capital letter m
🆕 squared new
🆖 squared ng
🅾 negative squared latin capital letter o
🆗 squared ok
🅿 negative squared latin capital letter p
🆘 squared sos
🆙 squared up with exclamation mark
🆚 squared vs
™ trade mark sign
© copyright sign
® registered sign
〰 wavy dash
➰ curly loop
➿ double curly loop
✳ eight spoked asterisk
✴ eight pointed black star
❇ sparkle
🔴 large red circle
🟠 large orange circle
🟡 large yellow circle
🟢 large green circle
🔵 large blue circle
🟣 large purple circle
🟤 large brown circle
⚫ medium black circle
⚪ medium white circle
🟥 large red square
🟧 large orange square
🟨 large yellow square
🟩 large green square
🟦 large blue square
🟪 large purple square
🟫 large brown square
⬛ black large square
⬜ white large square
◼ black medium square
◻ white medium square
◾ black medium small square
◽ white medium small square
▪ black small square
▫ white small square
🔶 large orange diamond
🔷 large blue diamond
🔸 small orange diamond
🔹 small blue diamond
🔺 up-pointing red triangle
🔻 down-pointing red triangle
💠 diamond shape with a dot inside
🔘 radio button
🔳 white square button
🔲 black square button
🏁 chequered flag
🚩 triangular flag on post
🎌 crossed flags
🏴 waving black flag
🏳 waving white flag
〽 part alternation mark
🛜 wireless
🪄 magic wand
🪅 piñata
🪆 nesting dolls
🪡 sewing needle
🪢 knot
🪙 coin
🪚 carpentry saw
🪛 screwdriver
🪤 mouse trap
🪣 bucket
🪥 toothbrush
🪞 mirror
🪟 window
🪠 plunger
🪧 placard
🛝 playground slide
🛞 wheel
🛟 ring buoy
🛻 pickup truck
🛼 roller skate
🥅 net
""";
}
