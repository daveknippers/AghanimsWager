<template>
  <div class="matches">
    <b-container>
      <b-row class="mt-4">
        <b-col style="display: flex; flex-wrap: wrap;">
          <b-card title="Best Bromance" img-top img-src="/assets/bestBromance.jpg" img-height="300px" tag="article"
            class="mb-2" style="flex: 1 0 300px;">
            <b-card-text>
              <h2>{{ bestBromance.bro1Name }}, {{ bestBromance.bro2Name }}</h2>
              <h2>Win Rate: {{ (bestBromance.winRate * 100).toFixed(2) }}%</h2>
              <p style="margin-bottom: 0px">We finish each other's sandwiches</p>
              <p style="font-size: smaller">(min. 3 games)</p>
            </b-card-text>
          </b-card>
          <b-card title="Worst Bromance" img-top img-src="/assets/worstBromance.png" img-height="300px" tag="article"
            class="mb-2" style="flex: 1 0 300px;">
            <b-card-text>
              <h2>{{ worstBromance.bro1Name }}, {{ worstBromance.bro2Name }}</h2>
              <h2>Win Rate: {{ (worstBromance.winRate * 100).toFixed(2) }}%</h2>
              <p style="margin-bottom: 0px">It may be time for some trust falls</p>
              <p style="font-size: smaller">(min. 3 games)</p>
            </b-card-text>
          </b-card>
        </b-col>
      </b-row>
      <b-row>
        <b-col style="display: flex; flex-wrap: wrap;">
          <div style="flex: 1 0 300px; max-width: 800px;" class="leaderboard">
            <h1> 
              <svg class="ico-cup">
                <use xlink:href="#cup"></use>
              </svg>
              Bromances
            </h1>
            <li class="flex-row" style="background-color: #474b53;">
              <span class="flex-child matches-header" style="flex: 0.5 1 350px; padding-left: 20px">Bros Discord Names</span>
              <span class="flex-child matches-header" style="flex: 1 1; text-align: center;">Won / Total Matches</span>
              <span class="flex-child matches-header" style="flex: 0.5 1; text-align: right; padding-right: 10px">Win
                Rate</span>
            </li>
            <ol>
              <li class="flex-row" v-for="bromance, index in bromances" :key="index">
                <span class="flex-child matches-descriptors">{{ bromance.bro1Name }}, {{ bromance.bro2Name }}</span>
                <span class="flex-child matches-data">{{ bromance.totalWins }} / {{ bromance.totalMatches }}</span>
                <small class="flex-child" style="text-align: right; flex: 0.3 1 100px">{{ (bromance.winRate *
                  100).toFixed(2) }}%</small>
              </li>
            </ol>
          </div>
        </b-col>
      </b-row>
    </b-container>
    <svg style="display: none;">
      <symbol id="cup" x="0px" y="0px" width="25px" height="26px" viewBox="0 0 25 26" enable-background="new 0 0 25 26"
        xml:space="preserve">
        <path fill="#6492F3" d="M21.215,1.428c-0.744,0-1.438,0.213-2.024,0.579V0.865c0-0.478-0.394-0.865-0.88-0.865H6.69
    C6.204,0,5.81,0.387,5.81,0.865v1.142C5.224,1.641,4.53,1.428,3.785,1.428C1.698,1.428,0,3.097,0,5.148
    C0,7.2,1.698,8.869,3.785,8.869h1.453c0.315,0,0.572,0.252,0.572,0.562c0,0.311-0.257,0.563-0.572,0.563
    c-0.486,0-0.88,0.388-0.88,0.865c0,0.478,0.395,0.865,0.88,0.865c0.421,0,0.816-0.111,1.158-0.303
    c0.318,0.865,0.761,1.647,1.318,2.31c0.686,0.814,1.515,1.425,2.433,1.808c-0.04,0.487-0.154,1.349-0.481,2.191
    c-0.591,1.519-1.564,2.257-2.975,2.257H5.238c-0.486,0-0.88,0.388-0.88,0.865v4.283c0,0.478,0.395,0.865,0.88,0.865h14.525
    c0.485,0,0.88-0.388,0.88-0.865v-4.283c0-0.478-0.395-0.865-0.88-0.865h-1.452c-1.411,0-2.385-0.738-2.975-2.257
    c-0.328-0.843-0.441-1.704-0.482-2.191c0.918-0.383,1.748-0.993,2.434-1.808c0.557-0.663,1-1.445,1.318-2.31
    c0.342,0.192,0.736,0.303,1.157,0.303c0.486,0,0.88-0.387,0.88-0.865c0-0.478-0.394-0.865-0.88-0.865
    c-0.315,0-0.572-0.252-0.572-0.563c0-0.31,0.257-0.562,0.572-0.562h1.452C23.303,8.869,25,7.2,25,5.148
    C25,3.097,23.303,1.428,21.215,1.428z M5.238,7.138H3.785c-1.116,0-2.024-0.893-2.024-1.99c0-1.097,0.908-1.99,2.024-1.99
    c1.117,0,2.025,0.893,2.025,1.99v2.06C5.627,7.163,5.435,7.138,5.238,7.138z M18.883,21.717v2.553H6.118v-2.553H18.883
    L18.883,21.717z M13.673,18.301c0.248,0.65,0.566,1.214,0.947,1.686h-4.24c0.381-0.472,0.699-1.035,0.947-1.686
    c0.33-0.865,0.479-1.723,0.545-2.327c0.207,0.021,0.416,0.033,0.627,0.033c0.211,0,0.42-0.013,0.627-0.033
    C13.195,16.578,13.344,17.436,13.673,18.301z M12.5,14.276c-2.856,0-4.93-2.638-4.93-6.273V1.73h9.859v6.273
    C17.43,11.638,15.357,14.276,12.5,14.276z M21.215,7.138h-1.452c-0.197,0-0.39,0.024-0.572,0.07v-2.06
    c0-1.097,0.908-1.99,2.024-1.99c1.117,0,2.025,0.893,2.025,1.99C23.241,6.246,22.333,7.138,21.215,7.138z" />
      </symbol>
    </svg>
  </div>
</template>

<script>
export default {
  name: 'MatchesPage',
  data() {
    return {
      bromances: []
    }
  },
  mounted() {
    this.getBromances();
  },
  computed: {
    bestBromance() {
      let reduced = {};
      if (this.bromances.length > 0) {
        reduced = this.bromances.reduce(function (a, b) {
          return ((a.totalMatches >= 3 && a.winRate > b.winRate) || (a.totalMatches >= 3 && a.winRate == b.winRate && a.totalWins > b.totalWins)) ? a : b;
        });
      }
      return reduced;
    },
    worstBromance() {
      let reduced = {};
      if (this.bromances.length > 0) {
        reduced = this.bromances.reduce(function (a, b) {
          return ((a.totalMatches >= 3 && a.winRate < b.winRate) || (a.totalMatches >= 3 && a.winRate == b.winRate && a.totalMatches > b.totalMatches)) ? a : b;
        });
      }
      return reduced;
    }
  },
  methods: {
    getBromances() {
      fetch('/api/DiscordCommands/Bromance')
        .then(function (response) {
          if (response.status != 200) {
            throw response.status;
          } else {
            return response.json();
          }
        }.bind(this))
        .then(function (data) {
          this.bromances = data.sort((a, b) => b.winRate - a.winRate);
        }.bind(this))
        .catch(error => {
          console.error(error);
        });
    }
  }
}

</script>

<!-- Add "scoped" attribute to limit CSS to this component only -->
<style scoped>
.flex-row {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-evenly;
}

div ::v-deep(.matches-header) {
  font-family: Arial, Helvetica, sans-serif;
  font-style: normal;
  font-weight: 400;
  font-size: 18px;
  text-align: left;
  vertical-align: middle;
  margin-top: 4px;
  color: white;
}

div ::v-deep(.matches-descriptors) {
  font-family: Arial, Helvetica, sans-serif;
  font-style: normal;
  font-weight: 400;
  font-size: 16px;
  text-align: left;
  vertical-align: middle;
  margin-top: 4px;
  flex: 1 0 300px;
}

div ::v-deep(.matches-data) {
  font-family: monospace;
  font-style: normal;
  font-weight: 500;
  font-size: 18px;
  text-align: center;
  vertical-align: middle;
  flex: 0.5 1 100px;
}

/*-------------------- Leaderboard --------------------*/
.leaderboard {
  background: linear-gradient(to bottom, #3a404d, #181c26);
  border-radius: 10px;
  box-shadow: 0 7px 30px rgba(62, 9, 11, .3);
  margin-bottom: 10px;
}

.leaderboard h1 {
  font-size: 24px;
  color: #e1e1e1;
  padding: 12px 13px 18px;
}

.leaderboard h1 svg {
  width: 25px;
  height: 26px;
  position: relative;
  top: 3px;
  margin-right: 6px;
  vertical-align: baseline;
}

.leaderboard ol {
  counter-reset: leaderboard;
  list-style-type: none;
  padding: 0;
}

.leaderboard ol li {
  z-index: 1;
  font-size: 20px;
  font-family: Arial, Helvetica, sans-serif;
  counter-increment: leaderboard;
  padding: 18px 10px 18px 50px;
  backface-visibility: hidden;
  transform: translateZ(0) scale(1, 1);
}

.leaderboard ol li::before {
  content: counter(leaderboard);
  position: absolute;
  z-index: 2;
  top: 15px;
  left: 15px;
  width: 25px;
  height: 25px;
  line-height: 25px;
  color: #6492f3;
  background: #fff;
  border-radius: 25px;
  text-align: center;
}

.leaderboard ol li span {
  z-index: 2;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  margin: 0;
  background: none;
  color: #fff;
}

.leaderboard ol li span::before,
.leaderboard ol li span::after {
  content: '';
  position: absolute;
  z-index: 1;
  bottom: -11px;
  left: -9px;
  border-top: 10px solid #5a86e4;
  border-left: 10px solid transparent;
  transition: all 0.1s ease-in-out;
  opacity: 0;
}

.leaderboard ol li span::after {
  left: auto;
  right: -9px;
  border-left: none;
  border-right: 10px solid transparent;
}

.leaderboard ol li small {
  z-index: 2;
  height: 100%;
  text-align: right;
  font-family: monospace;
  font-style: normal;
  font-weight: 500;
  font-size: 16px;
  flex: 0.5 1 100px;
  color: #fff;
}

.leaderboard ol li::after {
  z-index: 1;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background: #5a86e4;
  box-shadow: 0 3px 0 rgba(0, 0, 0, .08);
  transition: all 0.3s ease-in-out;
  opacity: 0;
}

.leaderboard ol li:nth-child(1) {
  background: #84abff;
}

.leaderboard ol li:nth-child(1)::after {
  background: #84abff;
}

.leaderboard ol li:nth-child(2) {
  background: #739cf3;
}

.leaderboard ol li:nth-child(2)::after {
  background: #739cf3;
  box-shadow: 0 2px 0 rgba(0, 0, 0, .08);
}

.leaderboard ol li:nth-child(2) span::before,
.leaderboard ol li:nth-child(2) span::after {
  border-top: 6px solid #739cf3;
  bottom: -7px;
}

.leaderboard ol li:nth-child(3) {
  background: #5985e6;
}

.leaderboard ol li:nth-child(3)::after {
  background: #5985e6;
  box-shadow: 0 1px 0 rgba(0, 0, 0, .11);
}

.leaderboard ol li:nth-child(3) span::before,
.leaderboard ol li:nth-child(3) span::after {
  border-top: 2px solid #5985e6;
  bottom: -3px;
}

.leaderboard ol li:nth-child(4) {
  background: #4877dd;
}

.leaderboard ol li:nth-child(4)::after {
  background: #4877dd;
  box-shadow: 0 -1px 0 rgba(0, 0, 0, .15);
}

.leaderboard ol li:nth-child(4) span::before,
.leaderboard ol li:nth-child(4) span::after {
  top: -7px;
  bottom: auto;
  border-top: none;
  border-bottom: 6px solid #4877dd;
}

.leaderboard ol li:nth-child(n+5) {
  background: #3366d3;
}

.leaderboard ol li:nth-child(n+5)::after {
  background: #3366d3;
  box-shadow: 0 -1px 0 rgba(0, 0, 0, .15);
}

.leaderboard ol li:nth-child(n+5) span::before,
.leaderboard ol li:nth-child(n+5) span::after {
  top: -7px;
  bottom: auto;
  border-top: none;
  border-bottom: 6px solid #3366d3;
}

.leaderboard ol li:nth-last-child(1) {
  background: #2459ca;
  border-radius: 0 0 10px 10px;
}

.leaderboard ol li:nth-last-child(1)::after {
  background: #2459ca;
  box-shadow: 0 -2.5px 0 rgba(0, 0, 0, .12);
  border-radius: 0 0 10px 10px;
}

.leaderboard ol li:nth-last-child(1) span::before,
.leaderboard ol li:nth-last-child(1) span::after {
  top: -9px;
  bottom: auto;
  border-top: none;
  border-bottom: 8px solid #2459ca;
}
</style>
