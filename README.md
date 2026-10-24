<div align="center">

<img src="docs/images/v2/brand/logo.png" alt="Runeboard" width="560">

<img src="docs/images/v2/brand/title.jpg" alt="Runeboard title screen" width="720">

<a href="https://github.com/josejuanmartinez/Runeboard/blob/main/docs/videos/trailer.mp4"><img src="docs/images/hero/trailer-preview.gif" alt="Watch the trailer" width="720"></a>

<sub>Click the preview above to watch the full trailer.</sub>

# Runeboard

*A dark, hex &amp; card-driven strategy game of armies, alliances, and ancient powers across Middle-earth.*

[**Website**](https://josejuanmartinez.github.io/Runeboard/) · [**Download**](https://github.com/josejuanmartinez/Runeboard/releases) · [**Dev Diary: The AI**](https://medium.com/@jjmcarrascosa/field-notes-dev-diary-on-video-games-ai-and-some-generative-ai-9d435055ae3f?postPublishedType=repub)

</div>

---

## Strategy with the soul of an old fantasy table

Runeboard brings a sweeping conflict to a living hex map. Muster distinct armies, guide legendary characters, claim strongholds, and play illustrated cards that can turn a desperate battle.

Every campaign grows from competing powers and shifting fronts. Roads, rivers, mountains, and hidden lands shape the choices before you — and the story left behind.

## A living tabletop

The war is played with miniatures on one continuous hand-painted map:

- **Live 3D figures** — every hero and captain is an animated figure on a painted base in its nation's colour.
- **171 hand-built settlements** — each town, hold and ruin has its own painting, and its own miniature rebuilt from it: Imladris, the seven circles of Minas Tirith, Hobbiton's smials, Orthanc, Barad-dûr…
- **A board that breathes** — drifting cloud shadows, chimney smoke, fog that rolls back as you explore, seasons that follow the calendar, and dusk while the rival nations move.

<div align="center">
<img src="docs/images/v2/settlements/imladris.png" width="170"><img src="docs/images/v2/settlements/minastirith.png" width="170"><img src="docs/images/v2/settlements/hobbiton.png" width="170"><img src="docs/images/v2/settlements/orthanc.png" width="170">
</div>

## Three fronts, one fate

<table>
<tr>
<td align="center" width="33%">
<img src="docs/images/v2/cards/gandalf.jpg" width="200"><br>
<b>The Free Peoples</b><br>
<sub>Rally scattered realms and stand against the gathering dark.</sub>
</td>
<td align="center" width="33%">
<img src="docs/images/v2/cards/sauron.jpg" width="200"><br>
<b>The Dark Lord</b><br>
<sub>Unleash vast hosts and break the strongholds of the Free People.</sub>
</td>
<td align="center" width="33%">
<img src="docs/images/v2/cards/saruman.jpg" width="200"><br>
<b>Neutral Nations</b><br>
<sub>Scheme, industrialize, and strike where rival powers are weakest.</sub>
</td>
</tr>
</table>

## Every move writes history

- **Shape your armies** — Recruit troops, combine forces, and march commanders across a dangerous world.
- **Play the moment** — Build a deck around your power and answer the battlefield with characters, actions, spells, and events.
- **Outthink rival powers** — Read the terrain, defend vital routes, and pursue objectives before the age turns against you.

## Take command of your campaign

1. **Pick a scenario** — From the classic random earth of *Champions of Middle Earth* to the authored 2950 T.A. story of *The Untold War of the Ring*.
2. **Browse the company** — Cycle through available leaders (Gandalf, Saruman, Sauron, and their many flavours), each with its own deck identity.
3. **Confirm your choice** — Start the campaign as that leader, with your capital, characters, and starting resources laid out on the hex map. Fog of war hides the rest of Middle-earth until you explore it.

Every character doubles as a card — hover a leader's name to see their full stats and portrait, or an underlined army name to see its card. Move hex by hex with the arrow keys or WASD; spotted enemies are traced in red, spotted neutrals in grey.

<div align="center">
<img src="docs/images/v2/shots/places-bloom.jpg" width="720"><br>
<sub>Carn Dûm, Angmar, Barad-dûr, Gorgoroth, the Misty Mountains — every place is a card too</sub>
</div>

## Gallery

<table>
<tr>
<td><img src="docs/images/v2/shots/shire-turn.jpg" width="380"></td>
<td><img src="docs/images/v2/shots/witch-king.jpg" width="380"></td>
</tr>
<tr>
<td><img src="docs/images/v2/shots/nazgul-hobbiton.jpg" width="380"></td>
<td><img src="docs/images/v2/shots/saruman.jpg" width="380"></td>
</tr>
<tr>
<td><img src="docs/images/v2/shots/objective.jpg" width="380"></td>
<td><img src="docs/images/v2/shots/leader-gandalf.jpg" width="380"></td>
</tr>
</table>

## AI

Every rival nation, and every character it commands, is played by a layered AI rather than a script: it reads the board, remembers what it has seen, sets itself missions, plans them, and checks its judgement against a neural network. Turn on **Watch Mode** and the whole war plays itself. The stack is [written up in detail here](https://medium.com/@jjmcarrascosa/field-notes-dev-diary-on-video-games-ai-and-some-generative-ai-9d435055ae3f?postPublishedType=repub):

| | |
|---|---|
| **Utility AI** | All nations score danger, opportunity, growth, and stagnation to weigh their options. |
| **Blackboards** | Store the situation of the world, and register/remember nation and character missions. |
| **Hierarchical Task Network** | Plans tasks for characters, decomposing them into specific possible behaviours. |
| **Behaviour / State Trees** | Execute behaviours depending on Utility AI scores and feasibility. |
| **Neural Network** | An MLP trained on supervised best strategies, consulted alongside the rest of the stack. |

<div align="center">
<img src="docs/images/gameplay/30-htn.webp" width="720"><br>
<sub>Utility AI scores, stored in Blackboards, are evaluated; tasks are planned and executed by (State) Behaviour Trees</sub>
</div>

## The art

Runeboard's retro illustration style started on paper, not in a prompt box:

1. Scanned old black & white illustrations from old RPG books.
2. Redrew and repainted some of them for a unique retro feeling.
3. Hand-painted 25 of them using colour schemes and techniques from the 80s.
4. Trained a LoRA on an open-source, open-weight Qwen Image model.
5. Queried that model — trained on the original art — to generate the game's cards.

Hexes started as assets from the [Unity Asset Store](https://assetstore.unity.com/packages/2d/environments/hex-medieval-fantasy-locations-59271), repainted by hand.

<div align="center">
<img src="docs/images/gameplay/35-lora-bw.png" width="360"><img src="docs/images/gameplay/36-lora-painted.png" width="360">
</div>

---

<div align="center">

An independent, non-commercial fan project inspired by classic fantasy strategy games.

[Website](https://josejuanmartinez.github.io/Runeboard/) · [Releases](https://github.com/josejuanmartinez/Runeboard/releases)

</div>
