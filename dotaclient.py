import logging, datetime, time, json
from collections import defaultdict

logging.basicConfig(format='[%(asctime)s] %(levelname)s %(name)s: %(message)s', level=logging.WARN)

import gevent

import sqlalchemy as db

from steam.client import SteamClient
from steam.enums import EPersonaState, EResult
from steam.enums.emsg import EMsg
from steam.utils.proto import proto_to_dict
from dota2.client import Dota2Client
from dota2.proto_enums import EDOTAGCMsg

from tokens import STEAM_BOT_ACCOUNT, STEAM_BOT_PASSWORD, CONNECTION_STRING

client = SteamClient()
dota = Dota2Client(client)

steam_id = 76561199107038141
SLEEPY_TIME = 10

source_tv_lobbies = set()
live_players_processed = defaultdict(lambda: False)
rp_heroes_processed = set()

def add_rp_hero(lobby_id,steam_id,rich_presence):
	if (lobby_id,steam_id) not in rp_heroes_processed:
		try:
			param0 = rich_presence['param0']
			param1 = rich_presence['param1']
			param2 = rich_presence['param2']

		except KeyError:
			logging.warn('could not parse rich presence for param[0,1,2] in lobby_id {} steam_id {}'.format(lobby_id,steam_id))
		else:
			row = {}
			row['lobby_id'] = lobby_id
			row['steam_id'] = steam_id
			row['param0'] = param0
			row['param1'] = param1
			row['param2'] = param2
			rp_heroes_processed.add((lobby_id,steam_id))
			if not pgdb.insert_rp(row):
				logging.warn('rp status already in database for lobby_id {} steam_id {}'.format(lobby_id,steam_id))

class PGDB:

	def __init__(self,conn_string):
		self.engine = db.create_engine(conn_string)
		self.conn = self.engine.connect()
		self.metadata = db.MetaData(schema='Kali')

		self.live_matches = db.Table('live_matches', self.metadata,
			db.Column('query_time',db.BigInteger,nullable=False),
			db.Column('activate_time',db.BigInteger,nullable=False),
			db.Column('deactivate_time',db.BigInteger,nullable=False),
			db.Column('server_steam_id',db.BigInteger,nullable=False),
			db.Column('lobby_id',db.BigInteger,nullable=False),
			db.Column('league_id',db.Integer,nullable=False),
			db.Column('lobby_type',db.Integer,nullable=False),
			db.Column('game_time',db.Integer,nullable=False),
			db.Column('delay',db.Integer,nullable=False),
			db.Column('spectators',db.Integer,nullable=False),
			db.Column('game_mode',db.Integer,nullable=False),
			db.Column('average_mmr',db.Integer,nullable=False),
			db.Column('match_id',db.BigInteger,nullable=False),
			db.Column('series_id',db.Integer,nullable=False),
			db.Column('sort_score',db.Integer,nullable=False),
			db.Column('last_update_time',db.Float,nullable=False),
			db.Column('radiant_lead',db.Integer,nullable=False),
			db.Column('radiant_score',db.Integer,nullable=False),
			db.Column('dire_score',db.Integer,nullable=False),
			db.Column('building_state',db.Integer,nullable=False))

		self.live_players = db.Table('live_players', self.metadata,
			db.Column('match_id',db.BigInteger,nullable=False),
			db.Column('player_num',db.Integer,nullable=False),
			db.Column('account_id',db.BigInteger,nullable=False),
			db.Column('hero_id',db.Integer,nullable=False))

		self.rp_heroes = db.Table('rp_heroes', self.metadata,
			db.Column('lobby_id',db.BigInteger,nullable=False,primary_key=True),
			db.Column('steam_id',db.BigInteger,nullable=False,primary_key=True),
			db.Column('param0',db.String,nullable=True,primary_key=True),
			db.Column('param1',db.String,nullable=True,primary_key=True),
			db.Column('param2',db.String,nullable=True))

		self.metadata.create_all(self.engine)

	def insert_lm(self,row):
		lm = self.live_matches
		insert = lm.insert().values(**row)
		result = self.conn.execute(insert)
	
	def insert_lp(self,players):
		lp = self.live_players
		insert = lp.insert().values(players)
		result = self.conn.execute(insert)

	def check_lp(self,match_id):
		lp = self.live_players
		s = db.select([lp]).where(lp.c.match_id == match_id)
		result = self.conn.execute(s).fetchall()
		if len(result) == 0:
			return False
		elif len(result != 10):
			logging.warn('Unexpected number of players in match_id {}'.format(match_id))
		return True

	def insert_rp(self,row):
		rp = self.rp_heroes
		s = db.select([rp]).where(db.and_(rp.c.lobby_id == row['lobby_id'],
										  rp.c.steam_id == row['steam_id']))
		if (result := self.conn.execute(s).fetchone()):
			return False
		else:
			insert = rp.insert().values(**row)
			result = self.conn.execute(insert)
			return True
			

@client.on(EMsg.ClientPersonaState)
def investigate_ClientPersonaState(msg):
	if client.friends.ready:
		source_tv_lobbies.clear()
		for f in client.friends:
			if f.state != EPersonaState.Offline:
				try:
					status = f.rich_presence['status']
					if status == '#DOTA_RP_PLAYING_AS':
						watchable_game_id = int(f.rich_presence['WatchableGameID'])
						steam_id = f.steam_id
						if watchable_game_id != 0:
							source_tv_lobbies.add(watchable_game_id)
							add_rp_hero(watchable_game_id,steam_id,f.rich_presence)
					elif status == '#DOTA_RP_HERO_SELECTION' or status == '#DOTA_RP_STRATEGY_TIME':
						watchable_game_id = int(f.rich_presence['WatchableGameID'])
						if watchable_game_id != 0:
							source_tv_lobbies.add(watchable_game_id)
				except KeyError:
					pass
	


@dota.on(EDOTAGCMsg.EMsgGCToClientFindTopSourceTVGamesResponse)
def process_lobby_states(msg):
	logging.info('Starting process_lobby_states')
	query_time = int(time.mktime(datetime.datetime.now().timetuple()))
	if msg.specific_games:	
		logging.info('Specific lobbies returned')
		for g in msg.game_list:
			game = proto_to_dict(g)
			match_id = game['match_id']
			game['query_time'] = query_time
			players = game['players']
			del game['players']
			pgdb.insert_lm(game)
			if not live_players_processed[match_id]:
				if pgdb.check_lp(match_id):
					live_players_processed[game['match_id']] = True
				elif not any(map(lambda x: x == 0,map(lambda x: x['hero_id'],players))):
					for p,i in zip(players,range(len(players))):
						p['match_id'] = match_id
						p['player_num'] = i
					pgdb.insert_lp(players)
					live_players_processed[game['match_id']] = True
	else:
		logging.info('No specific lobbies returned')

@client.on('logged_on')
def start_dota():
	logging.info('Starting Dota GC communication')
	dota.launch()

#@dota.on('notready')

@dota.on('ready')
def lobby_loop():
	logging.info('Dota GC communications prepared')
	while True:
		gevent.sleep(SLEEPY_TIME)
		logging.info('Checking lobbies...')
		check_time = datetime.datetime.now()
		n_lobbies = len(source_tv_lobbies)
		if n_lobbies > 0:
			data = dict([('lobby_ids',list(source_tv_lobbies))])
			logging.info('Checking {} lobbies'.format(n_lobbies))
			dota.send(EDOTAGCMsg.EMsgClientToGCFindTopSourceTVGames,data)
		else:
			logging.info('No lobbies found')

@client.on('disconnect')
def handle_disconnect():
	logging.warn('Steam disconnected')
	if client.relogin_available:
		client.reconnect

pgdb = PGDB(CONNECTION_STRING)


with open('heroes.json') as f:
	hero_data = json.load(f)
	hero_data = dict(map(lambda x: (x['name'],(x['id'],x['localized_name'])),hero_data['heroes']))
	
result = client.cli_login(username=STEAM_BOT_ACCOUNT,password=STEAM_BOT_PASSWORD)
if result != EResult.OK:
	logging.error('Could not log in')	
	raise SystemExit
client.run_forever()

